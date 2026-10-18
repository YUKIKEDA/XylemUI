using System.Runtime.CompilerServices;

namespace XylemUI;

internal static partial class LayoutEngine
{
    // A node without children (Taffy's compute_leaf_layout): its known or styled size on each axis, otherwise its content's (the
    // measure function's, 0 without one) plus its padding and border; clamped by its minimum and maximum, and never smaller than
    // its padding and border. Its first baseline is its content's, below the padding and border.
    private static Size ComputeLeaf(in LayoutStyle style, MeasureFunc? measure, in LayoutInput input, bool commit, out float baseline)
    {
        baseline = float.NaN;
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
            MeasureResult measured = measure(knownContent, available);
            content = measured.Size;
            baseline = paddingBorder.Top + measured.FirstBaseline;
        }

        OptionalSize size = nodeSize.Or(new OptionalSize(content.Width + inset.Width, content.Height + inset.Height)).Clamp(min, max).AtLeast(inset);
        return ToSize(size);
    }

    // The border-box width of a leaf under a min-content or max-content width, as ComputeLeaf gives it, from the content
    // widths: kept on the node, or returned by its IntrinsicWidthsFunc, which counts as a run of the leaf.
    private static float LeafIntrinsicWidth(UiTree tree, NodeHandle handle, in LayoutInput input)
    {
        ref UiNode node = ref tree.Nodes.GetRef(handle);
        ref readonly LayoutStyle style = ref node.Layout.Style;
        bool hasBox = node.Layout.HasBox;
        ref readonly ResolvedBox box = ref node.Layout.Box;
        Edges paddingBorder = hasBox ? box.PaddingBorder : PaddingBorder(in style, input.ParentSize.Width);
        float inset = paddingBorder.Horizontal;
        float width = float.NaN;
        float min = float.NaN;
        float max = float.NaN;
        if (input.SizingMode == SizingMode.InherentSize)
        {
            width = hasBox ? box.Size.Width : StyleSize(in style, input.ParentSize).Width;
            min = hasBox ? box.Min.Width : MinSize(in style, input.ParentSize).Width;
            max = hasBox ? box.Max.Width : MaxSize(in style, input.ParentSize).Width;
        }

        if (float.IsNaN(width))
        {
            float knownHeight = input.KnownDimensions.Height - paddingBorder.Vertical;
            if (!tree.MeasuresOf(handle).TryGetIntrinsicWidths(knownHeight, out IntrinsicWidths widths))
            {
                IntrinsicWidthsFunc intrinsic = node.Layout.IntrinsicWidths!;
#if DEBUG
                MeasureFunc measure = node.Layout.Measure!;
#endif
                tree.LayoutRunHook?.Invoke(handle);
                widths = intrinsic(knownHeight);
#if DEBUG
                VerifyIntrinsicWidths(measure, knownHeight, widths);
#endif
                tree.MeasuresOf(handle).StoreIntrinsicWidths(knownHeight, widths);
            }

            width = (input.Available.Width.Kind == AvailableSpaceKind.MinContent ? widths.Min : widths.Max) + inset;
        }

        return Math.Max(Maybe.Clamp(width, min, max), inset);
    }

#if DEBUG
    // Debug builds check a leaf's two functions against each other each time its content widths are computed: an
    // IntrinsicWidthsFunc that disagrees with the leaf's MeasureFunc under a min-content and a max-content width lays the
    // leaf out wrong without a sign (PR #103's review). Release builds do not call the measure function here.
    private static void VerifyIntrinsicWidths(MeasureFunc measure, float knownHeight, IntrinsicWidths widths)
    {
        var known = new OptionalSize(float.NaN, knownHeight);
        AvailableSpace height = float.IsNaN(knownHeight) ? AvailableSpace.MaxContent : AvailableSpace.Definite(knownHeight);
        float min = measure(known, new AvailableSize(AvailableSpace.MinContent, height)).Size.Width;
        float max = measure(known, new AvailableSize(AvailableSpace.MaxContent, height)).Size.Width;
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
}
