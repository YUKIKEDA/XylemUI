using System.Runtime.CompilerServices;

namespace XylemUI;

internal static partial class LayoutEngine
{
    // A node without children (Taffy's compute_leaf_layout): its known or styled size on each axis, otherwise its content's (the
    // measure function's, 0 without one) plus its padding and border; clamped by its minimum and maximum, and never smaller than
    // its padding and border. Its first baseline is its content's, below the padding and border. A scrollbar's room (Overflow.Scroll)
    // is taken from the content box, inside the padding (Taffy's leaf.rs, the scrollbar gutter).
    private static Size ComputeLeaf(
        UiTree tree,
        NodeHandle handle,
        in LayoutStyle style,
        MeasureFunc? measure,
        TextLeaf? text,
        in LayoutInput input,
        bool commit,
        out float baseline
    )
    {
        baseline = float.NaN;
        Edges paddingBorder = PaddingBorder(in style, input.ParentSize.Width);
        Size inset = paddingBorder.Sum();
        Size gutter = style.ScrollbarGutter;
        var contentInset = new Size(inset.Width + gutter.Width, inset.Height + gutter.Height);

        // A content-size run ignores the node's own size styles (CSS Flexbox §4.5, the content size suggestion).
        OptionalSize nodeSize = input.KnownDimensions;
        OptionalSize min = OptionalSize.None;
        OptionalSize max = OptionalSize.None;
        if (input.SizingMode == SizingMode.InherentSize)
        {
            nodeSize = nodeSize.Or(StyleSize(in style, input.ParentSize));
            min = MinSize(in style, input.ParentSize);
            max = MaxSizeIgnoringAspectRatio(in style, input.ParentSize);
        }

        if (!commit && nodeSize.IsComplete)
        {
            return ToSize(nodeSize.Clamp(min, max).AtLeast(inset));
        }

        var available = new AvailableSize(
            ContentSpace(input.Available.Width.OrDefinite(nodeSize.Width), min.Width, max.Width, contentInset.Width),
            ContentSpace(input.Available.Height.OrDefinite(nodeSize.Height), min.Height, max.Height, contentInset.Height)
        );
        Size content = default;
        if (measure is not null || text is not null)
        {
            OptionalSize knownContent = commit ? OptionalSize.None : input.KnownDimensions.Minus(contentInset);
            MeasureResult measured = MeasureContent(tree, handle, measure, text, knownContent, available, commit);
            content = measured.Size;
            baseline = paddingBorder.Top + measured.FirstBaseline;
        }

        OptionalSize size = nodeSize.Or(new OptionalSize(content.Width + contentInset.Width, content.Height + contentInset.Height)).Clamp(min, max);
        if (commit)
        {
            // The content from the padding box's corner; a scroll container's end padding is part of it (Taffy main's leaf.rs).
            Edges padding = EdgeInsets.ResolveOrZero(style.Padding, input.ParentSize.Width);
            bool scrolls = style.IsScrollContainer;
            ref MeasureCache cache = ref tree.MeasuresOf(handle);
            cache.ContentRight = padding.Left + content.Width + (scrolls ? padding.Right : 0);
            cache.ContentBottom = padding.Top + content.Height + (scrolls ? padding.Bottom : 0);
        }

        // A leaf with an aspect ratio is at least as high as its width gives (Taffy's compute_leaf_layout); its maximum does not
        // follow the ratio there.
        if (input.SizingMode == SizingMode.InherentSize && style.HasAspectRatio)
        {
            size = size with { Height = Math.Max(size.Height, size.Width / style.AspectRatio) };
        }

        return ToSize(size.AtLeast(inset));
    }

    // The border-box width of a leaf under a min-content or max-content width, as ComputeLeaf gives it, from the content
    // widths: kept on the node, or returned by its IntrinsicWidthsFunc, which counts as a run of the leaf.
    private static float LeafIntrinsicWidth(UiTree tree, NodeHandle handle, in LayoutInput input)
    {
        ref UiNode node = ref tree.Nodes.GetRef(handle);
        ref readonly LayoutStyle style = ref node.Layout.Style;
        bool hasBox = node.Layout.HasBox;
        ref readonly ResolvedBox box = ref tree.MeasuresOf(handle).Box;
        Edges paddingBorder = hasBox ? box.PaddingBorder : PaddingBorder(in style, input.ParentSize.Width);
        float inset = paddingBorder.Horizontal;

        // The content widths are the content box's; a scrollbar's room is added to them, not to the floor of the padding and border.
        float contentInset = inset + style.ScrollbarGutter.Width;
        float width = float.NaN;
        float min = float.NaN;
        float max = float.NaN;
        if (input.SizingMode == SizingMode.InherentSize)
        {
            width = hasBox ? box.Size.Width : StyleSize(in style, input.ParentSize).Width;
            min = hasBox ? box.Min.Width : MinSize(in style, input.ParentSize).Width;
            max = hasBox ? box.Max.Width : MaxSizeIgnoringAspectRatio(in style, input.ParentSize).Width;
        }

        if (float.IsNaN(width))
        {
            float knownHeight = input.KnownDimensions.Height - paddingBorder.Vertical - style.ScrollbarGutter.Height;
            if (!tree.MeasuresOf(handle).TryGetIntrinsicWidths(knownHeight, out IntrinsicWidths widths))
            {
                widths = ComputeIntrinsicWidths(tree, handle, knownHeight);
            }

            width = (input.Available.Width.Kind == AvailableSpaceKind.MinContent ? widths.Min : widths.Max) + contentInset;
        }

        return Math.Max(Maybe.Clamp(width, min, max), inset);
    }

    // Asks a leaf for its content widths for a content height of knownHeight (NaN when it is not decided) and keeps them: a text
    // leaf from its text shaped once, another leaf from its IntrinsicWidthsFunc.
    private static IntrinsicWidths ComputeIntrinsicWidths(UiTree tree, NodeHandle handle, float knownHeight)
    {
        ref UiNode node = ref tree.Nodes.GetRef(handle);
        IntrinsicWidthsFunc? intrinsic = node.Layout.IntrinsicWidths;
#if DEBUG
        MeasureFunc? measure = node.Layout.Measure;
#endif
        TextLeaf? text = node.Text;
        tree.LayoutRunHook?.Invoke(handle);
        IntrinsicWidths widths;
        try
        {
            widths = text is not null ? TextIntrinsicWidths(tree.Text!, text) : intrinsic!(knownHeight);
        }
        catch (Exception exception) when (exception is not InvariantViolationException)
        {
            throw new LeafMeasureException(handle, exception);
        }
#if DEBUG
        VerifyIntrinsicWidths(tree, measure, text, knownHeight, widths);
#endif
        if (tree.MeasuresOf(handle).StoreIntrinsicWidths(knownHeight, widths))
        {
            NoteOverwrite(tree, handle);
        }

        return widths;
    }

#if DEBUG
    // Debug builds check a leaf's two functions against each other each time its content widths are computed: an
    // IntrinsicWidthsFunc that disagrees with the leaf's MeasureFunc under a min-content and a max-content width lays the
    // leaf out wrong without a sign (PR #103's review). Release builds do not call the measure function here.
    private static void VerifyIntrinsicWidths(UiTree tree, MeasureFunc? measure, TextLeaf? text, float knownHeight, IntrinsicWidths widths)
    {
        var known = new OptionalSize(float.NaN, knownHeight);
        AvailableSpace height = float.IsNaN(knownHeight) ? AvailableSpace.MaxContent : AvailableSpace.Definite(knownHeight);
        var minSpace = new AvailableSize(AvailableSpace.MinContent, height);
        var maxSpace = new AvailableSize(AvailableSpace.MaxContent, height);
        float min;
        float max;
        if (text is not null)
        {
            // Measured apart from the leaf's cache, which the check must not fill with widths the layout never asked for.
            min = MeasureTextUncached(tree.Text!, text, AvailableSpace.MinContent);
            max = MeasureTextUncached(tree.Text!, text, AvailableSpace.MaxContent);
        }
        else
        {
            min = measure!(known, minSpace).Size.Width;
            max = measure!(known, maxSpace).Size.Width;
        }

        if (Math.Abs(min - widths.Min) > 0.01f || Math.Abs(max - widths.Max) > 0.01f)
        {
            Invariant.Fail(
                FormattableString.Invariant(
                    $"A leaf's content widths ({widths.Min}, {widths.Max}) disagree with its measure function ({min}, {max})."
                )
            );
        }
    }
#endif

    // The space for the content box: a definite border-box space clamped by the node's minimum and maximum, less its padding
    // and border. A content constraint stays.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static AvailableSpace ContentSpace(AvailableSpace space, float min, float max, float inset) =>
        space.IsDefinite ? AvailableSpace.Definite(Maybe.Clamp(space.Value, min, max) - inset) : space;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Size ToSize(OptionalSize size) => new(size.Width, size.Height);

    // A leaf's content: its text, or its measure function's result. What either throws is wrapped with the leaf, so that the pass
    // reports the same failure of the same leaf once (design §2.4.1, the exceptions of a layout).
    private static MeasureResult MeasureContent(
        UiTree tree,
        NodeHandle handle,
        MeasureFunc? measure,
        TextLeaf? text,
        OptionalSize known,
        AvailableSize available,
        bool commit
    )
    {
        try
        {
            return text is not null ? MeasureText(tree.Text!, text, known, available, commit) : measure!(known, available);
        }
        catch (Exception exception) when (exception is not InvariantViolationException and not LeafMeasureException)
        {
            throw new LeafMeasureException(handle, exception);
        }
    }
}
