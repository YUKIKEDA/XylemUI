namespace XylemUI;

internal static partial class LayoutEngine
{
    // A virtualized list's box (design §3.4, "The shape of P02e", the layout `LayoutAlgorithm.Virtual`): its size as a block's, with
    // an automatic length along the axis that scrolls taken from all the rows, measured or estimated; its rows laid out across the
    // content box, each as long as its content, and placed at the position its index has, after the gaps of the rows before it. Only
    // the rows the list lays out are laid out; a row it keeps outside the range keeps its last box. Flutter's SliverList gives its
    // children the cross axis as a tight constraint (rendering/sliver_list.dart line 56, sliver.dart lines 492 to 500).
    private static Size ComputeVirtual(
        UiTree tree,
        NodeHandle handle,
        in LayoutStyle style,
        VirtualProps props,
        NodeHandle firstChild,
        in LayoutInput input,
        bool commit
    )
    {
        IVirtualHost? host = tree.VirtualHostOf(handle);
        bool horizontal = props.Horizontal;
        OptionalSize parent = input.ParentSize;
        Edges paddingBorder = PaddingBorder(in style, parent.Width);
        Size inset = paddingBorder.Sum();
        Edges content = style.HasScrollbarGutter ? WithGutter(paddingBorder, in style) : paddingBorder;
        Size contentInset = content.Sum();
        OptionalSize min = MinSize(in style, parent);
        OptionalSize max = MaxSize(in style, parent);
        OptionalSize styleSize = StyleSize(in style, parent);
        OptionalSize clampedStyleSize = input.SizingMode == SizingMode.InherentSize ? styleSize.Clamp(min, max) : OptionalSize.None;
        OptionalSize known = input.KnownDimensions.Or(MinMaxDefinite(min, max)).Or(clampedStyleSize).AtLeast(inset);

        // Across: the size given, the available size, or the widest row laid out with its content's size.
        float cross = horizontal ? known.Height : known.Width;
        float crossInset = horizontal ? contentInset.Height : contentInset.Width;
        if (float.IsNaN(cross))
        {
            AvailableSpace available = horizontal ? input.Available.Height : input.Available.Width;
            cross = available.IsDefinite ? available.Value : WidestRow(tree, host, firstChild, horizontal) + crossInset;
            cross = Math.Max(
                Maybe.Clamp(cross, horizontal ? min.Height : min.Width, horizontal ? max.Height : max.Width),
                horizontal ? inset.Height : inset.Width
            );
        }

        float innerCross = Math.Max(0, cross - crossInset);
        float innerMainBasis = horizontal ? known.Width - contentInset.Width : known.Height - contentInset.Height;
        Edges padding = EdgeInsets.ResolveOrZero(style.Padding, parent.Width);
        Edges border = EdgeInsets.ResolveOrZero(style.Border, parent.Width);
        float gap = props.Gap.ResolveOrZero(Maybe.Or(innerMainBasis, 0));
        host?.BeginLayout(gap, horizontal ? padding.Left : padding.Top, horizontal ? padding.Right : padding.Bottom);

        float mainInset = horizontal ? contentInset.Width : contentInset.Height;
        float main = horizontal ? known.Width : known.Height;
        if (float.IsNaN(main))
        {
            main = mainInset + (host?.ContentLength() ?? 0);
            main = Math.Max(
                Maybe.Clamp(main, horizontal ? min.Width : min.Height, horizontal ? max.Width : max.Height),
                horizontal ? inset.Width : inset.Height
            );
        }

        Size size = horizontal ? new Size(main, cross) : new Size(cross, main);
        if (!commit || host is null)
        {
            return size;
        }

        // The rows first, so that the lengths the positions add up are this layout's; then each at its position.
        float? fixedExtent = host.FixedExtent;
        for (NodeHandle child = firstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
        {
            if (!host.LaysOut(child, out int index))
            {
                continue;
            }

            Edges margin = EdgeInsets.ResolveOrZero(tree.Nodes.GetRef(child).Layout.Style.Margin, innerCross);
            float marginCross = horizontal ? margin.Vertical : margin.Horizontal;
            float marginMain = horizontal ? margin.Horizontal : margin.Vertical;
            float rowCross = Math.Max(0, innerCross - marginCross);
            float rowMain = fixedExtent is { } length ? Math.Max(0, length - marginMain) : float.NaN;
            LayoutInput rowInput = RowInput(horizontal, rowCross, rowMain, innerCross);
            Size laid = LayoutChild(tree, child, in rowInput, commit: true);
            host.Measured(index, (horizontal ? laid.Width : laid.Height) + marginMain);
        }

        float startMain = horizontal ? border.Left + padding.Left : border.Top + padding.Top;
        float startCross = horizontal ? content.Top : content.Left;
        for (NodeHandle child = firstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
        {
            if (!host.LaysOut(child, out int index))
            {
                continue;
            }

            Edges margin = EdgeInsets.ResolveOrZero(tree.Nodes.GetRef(child).Layout.Style.Margin, innerCross);
            float position = startMain + host.PositionOf(index);
            if (horizontal)
            {
                Place(tree, child, position + margin.Left, startCross + margin.Top);
            }
            else
            {
                Place(tree, child, startCross + margin.Left, position + margin.Top);
            }
        }

        return size;
    }

    // What a row is given: its size across, fixed, and along the axis that scrolls the fixed extent's or its content's.
    private static LayoutInput RowInput(bool horizontal, float cross, float main, float innerCross)
    {
        AvailableSpace along = float.IsNaN(main) ? AvailableSpace.MaxContent : AvailableSpace.Definite(main);
        return horizontal
            ? new LayoutInput(
                new OptionalSize(main, cross),
                KnownWidthIsDefinite: !float.IsNaN(main),
                KnownHeightIsDefinite: true,
                new OptionalSize(float.NaN, innerCross),
                new AvailableSize(along, AvailableSpace.Definite(cross)),
                SizingMode.InherentSize,
                RequestedAxis.Both
            )
            : new LayoutInput(
                new OptionalSize(cross, main),
                KnownWidthIsDefinite: true,
                KnownHeightIsDefinite: !float.IsNaN(main),
                new OptionalSize(innerCross, float.NaN),
                new AvailableSize(AvailableSpace.Definite(cross), along),
                SizingMode.InherentSize,
                RequestedAxis.Both
            );
    }

    // The widest of the rows laid out, measured with their content's size, margins included: the size across of a box that is given
    // none.
    private static float WidestRow(UiTree tree, IVirtualHost? host, NodeHandle firstChild, bool horizontal)
    {
        if (host is null)
        {
            return 0;
        }

        float widest = 0;
        var measure = new LayoutInput(
            OptionalSize.None,
            KnownWidthIsDefinite: false,
            KnownHeightIsDefinite: false,
            OptionalSize.None,
            AvailableSize.MaxContent,
            SizingMode.InherentSize,
            horizontal ? RequestedAxis.Vertical : RequestedAxis.Horizontal
        );
        for (NodeHandle child = firstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
        {
            if (!host.LaysOut(child, out _))
            {
                continue;
            }

            Edges margin = EdgeInsets.ResolveOrZero(tree.Nodes.GetRef(child).Layout.Style.Margin, float.NaN);
            Size measured = LayoutChild(tree, child, in measure, commit: false);
            widest = Math.Max(widest, horizontal ? measured.Height + margin.Vertical : measured.Width + margin.Horizontal);
        }

        return widest;
    }
}
