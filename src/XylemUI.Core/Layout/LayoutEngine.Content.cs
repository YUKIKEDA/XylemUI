namespace XylemUI;

internal static partial class LayoutEngine
{
    // Row L5 (design §2.4.1): after a leaf whose content changed was laid out again with its last input, compares what its
    // parent read with the leaf's new values. When every value is the same, the parent's results stay and the parent is not laid
    // out; otherwise the path from the parent to the nearest boundary is marked as MarkDirty marks it, and the worklist goes on
    // with it in the same pass. A comparison that throws counts as a difference.
    private static void CompareContentChange(UiTree tree, NodeHandle handle)
    {
        int index = tree.FindContentSnapshot(handle);
        if (index < 0)
        {
            return;
        }

        ContentSnapshot snapshot = tree.ContentSnapshots[index];
        tree.RemoveContentSnapshotAt(index);
        bool same = false;
        try
        {
            same = SameReads(tree, handle, in snapshot);
        }
        finally
        {
            if (same)
            {
                // The parent's base sizes came from the same values. A slot written since was written by a run of the
                // parent, which knew the new content.
                ref MeasureCache measures = ref tree.MeasuresOf(handle);
                if (!measures.MeasureBase.Valid)
                {
                    measures.MeasureBase = snapshot.Measures.MeasureBase;
                }

                if (!measures.LayoutBase.Valid)
                {
                    measures.LayoutBase = snapshot.Measures.LayoutBase;
                }
            }
            else
            {
                Invalidation.MarkPath(tree, tree.Nodes.GetRef(handle).Parent, snapshot.Flags);
            }
        }
    }

    // The leaf's size and first baseline from the layout run just done, then each kept measurement measured again with its
    // key, then the content widths asked again for each kept height. The new values fill the leaf's caches, which were dropped
    // when it was marked.
    private static bool SameReads(UiTree tree, NodeHandle handle, in ContentSnapshot snapshot)
    {
        ref UiNode node = ref tree.Nodes.GetRef(handle);
        if (
            ((node.Dirty.SelfDirty | node.Dirty.SubtreeDirty) & DirtyFlags.Layout) != 0
            || !node.FirstChild.IsNone
            || node.Layout.Rect.Width != snapshot.Size.Width
            || node.Layout.Rect.Height != snapshot.Size.Height
            || !Maybe.Same(node.Layout.FirstBaseline, snapshot.FirstBaseline)
        )
        {
            return false;
        }

        bool hasIntrinsicWidths = node.Layout.IntrinsicWidths is not null;
        ref readonly MeasureCache kept = ref snapshot.Measures;
        for (int i = 0; i < kept.Count; i++)
        {
            kept.GetEntry(i, out LayoutCacheKey key, out RequestedAxis axis, out Size size);
            var input = new LayoutInput(
                key.Known,
                key.KnownWidthIsDefinite,
                key.KnownHeightIsDefinite,
                key.Parent,
                key.Available,
                key.SizingMode,
                axis
            );
            Size now = Compute(tree, handle, in input, commit: false);
            bool width = axis == RequestedAxis.Vertical || now.Width == size.Width;
            bool height = axis == RequestedAxis.Horizontal || now.Height == size.Height;
            if (!width || !height)
            {
                return false;
            }
        }

        if (hasIntrinsicWidths)
        {
            if (kept.TryGetAnyHeightWidths(out IntrinsicWidths any) && !SameWidths(ComputeIntrinsicWidths(tree, handle, float.NaN), any))
            {
                return false;
            }

            if (
                kept.TryGetAtHeightWidths(out float knownHeight, out IntrinsicWidths atHeight)
                && !SameWidths(ComputeIntrinsicWidths(tree, handle, knownHeight), atHeight)
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameWidths(IntrinsicWidths a, IntrinsicWidths b) =>
        a.Min == b.Min && a.Max == b.Max && a.DependsOnHeight == b.DependsOnHeight;
}
