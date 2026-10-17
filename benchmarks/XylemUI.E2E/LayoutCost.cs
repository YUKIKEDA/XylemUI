using System.Diagnostics;
using System.Runtime.CompilerServices;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The layout costs of design §3.2's acceptance (roadmap row L1a): a column of 100 rows of 100 leaves (10,101 nodes), with the
// four times S6b measured: the first layout, a relayout after one leaf changes, a change inside a row of fixed size, and a
// window width change. Three scenes: S6b's meaning (no automatic minimum: every flex item has a minimum of 0), CSS's
// defaults (the automatic minimum measures every item's min-content size), and one that also uses percentages and padding.
internal static class LayoutCost
{
    private const int Rows = 100;
    private const int Columns = 100;

    public static int Run(int samples)
    {
        UiThread.Initialize();
        using var report = new Report("layout");
        report.Number("nodes", 1 + (Rows * (Columns + 1)));
        report.Number("ui_node_bytes", Unsafe.SizeOf<UiNode>());
        report.Number("measure_cache_entries", MeasureCache.Capacity);
        report.Number("try_get_ns", TryGetNanoseconds());
        foreach (string scene in new[] { "s6b", "auto_min", "percent" })
        {
            Measure(report, scene, samples);
        }

        return 0;
    }

    // One case repeated on one tree, so that a profile of the process shows that case (the cpu-profile skill): `change` a
    // relayout after one leaf changes, `width` a window width change.
    public static int Loop(string scene, string kind, int count)
    {
        UiThread.Initialize();
        (UiTree tree, NodeHandle root, NodeHandle target) = Build(scene, fixedRows: false);
        Invalidation.NotifyWindowResized(tree, root, new Size(2_000, 2_000));
        LayoutEngine.RunLayoutPass(tree);
        LayoutStyle style = tree.Nodes.GetRef(target).Layout.Style;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < count; i++)
        {
            if (kind == "width")
            {
                Invalidation.NotifyWindowResized(tree, root, new Size(i % 2 == 0 ? 1_600 : 2_000, 2_000));
            }
            else
            {
                tree.SetStyle(target, style with { Width = Length.Px(i % 2 == 0 ? 20 : 10) });
            }

            LayoutEngine.RunLayoutPass(tree);
        }

        Console.WriteLine($"{scene} {kind}: {Stopwatch.GetElapsedTime(start).TotalMicroseconds / count:F2} us a pass");
        return 0;
    }

    private static void Measure(Report report, string scene, int samples)
    {
        var first = new List<double>(samples);
        var change = new List<double>(samples);
        var fixedRow = new List<double>(samples);
        var width = new List<double>(samples);
        for (int s = 0; s < samples + 3; s++)
        {
            bool record = s >= 3;
            (UiTree tree, NodeHandle root, NodeHandle target) = Build(scene, fixedRows: false);
            long start = Stopwatch.GetTimestamp();
            Invalidation.NotifyWindowResized(tree, root, new Size(2_000, 2_000));
            LayoutEngine.RunLayoutPass(tree);
            Add(first, start, record);

            LayoutStyle style = tree.Nodes.GetRef(target).Layout.Style;
            for (int i = 0; i < 2; i++)
            {
                tree.SetStyle(target, style with { Width = Length.Px(i % 2 == 0 ? 20 : 10) });
                start = Stopwatch.GetTimestamp();
                LayoutEngine.RunLayoutPass(tree);
                Add(change, start, record && i == 1);
            }

            for (int i = 0; i < 2; i++)
            {
                start = Stopwatch.GetTimestamp();
                Invalidation.NotifyWindowResized(tree, root, new Size(i % 2 == 0 ? 1_600 : 2_000, 2_000));
                LayoutEngine.RunLayoutPass(tree);
                Add(width, start, record && i == 1);
            }

            (UiTree fixedTree, NodeHandle fixedRoot, NodeHandle fixedTarget) = Build(scene, fixedRows: true);
            Invalidation.NotifyWindowResized(fixedTree, fixedRoot, new Size(2_000, 2_000));
            LayoutEngine.RunLayoutPass(fixedTree);
            LayoutStyle fixedStyle = fixedTree.Nodes.GetRef(fixedTarget).Layout.Style;
            for (int i = 0; i < 2; i++)
            {
                fixedTree.SetStyle(fixedTarget, fixedStyle with { Width = Length.Px(i % 2 == 0 ? 20 : 10) });
                start = Stopwatch.GetTimestamp();
                LayoutEngine.RunLayoutPass(fixedTree);
                Add(fixedRow, start, record && i == 1);
            }
        }

        // How many times a node's algorithm or measure function ran, in one first layout and in one relayout after a change: the
        // figures that explain the times.
        (UiTree countTree, NodeHandle countRoot, NodeHandle countTarget) = Build(scene, fixedRows: false);
        int runs = 0;
        countTree.LayoutRunHook = _ => runs++;
        Invalidation.NotifyWindowResized(countTree, countRoot, new Size(2_000, 2_000));
        LayoutEngine.RunLayoutPass(countTree);
        report.Number($"{scene}_first_runs", runs);
        runs = 0;
        countTree.SetStyle(countTarget, countTree.Nodes.GetRef(countTarget).Layout.Style with { Width = Length.Px(20) });
        LayoutEngine.RunLayoutPass(countTree);
        report.Number($"{scene}_change_runs", runs);

        report.Series($"{scene}_first_us", first);
        report.Series($"{scene}_change_us", change);
        report.Series($"{scene}_fixed_row_change_us", fixedRow);
        report.Series($"{scene}_width_us", width);
    }

    // The cost of copying a node out of the arena (Arena.TryGet returns the value): it grows with the measurement cache.
    private static double TryGetNanoseconds()
    {
        (UiTree tree, NodeHandle root, _) = Build("s6b", fixedRows: false);
        const int Calls = 1_000_000;
        float sink = 0;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < Calls; i++)
        {
            if (tree.Nodes.TryGet(root, out UiNode node))
            {
                sink += node.Layout.Rect.Width;
            }
        }

        double nanoseconds = Stopwatch.GetElapsedTime(start).TotalNanoseconds / Calls;
        return sink >= 0 ? nanoseconds : -1;
    }

    private static void Add(List<double> times, long start, bool record)
    {
        if (record)
        {
            times.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
        }
    }

    // The leaves measure 10 x 10 (a fixed content, so the time is the layout's, not a measure function's).
    private static (UiTree Tree, NodeHandle Root, NodeHandle Target) Build(string scene, bool fixedRows)
    {
        var tree = new UiTree();
        NodeHandle root = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Column });
        NodeHandle target = default;
        var noMinimum = LengthPercentageAuto.Px(0);
        LayoutStyle rowStyle = new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Row } with
        {
            MinHeight = scene == "s6b" ? noMinimum : LengthPercentageAuto.Auto,
        };
        if (fixedRows)
        {
            rowStyle = rowStyle with { Width = Length.Px(2_000), Height = Length.Px(10) };
        }

        LayoutStyle leafStyle = scene switch
        {
            "s6b" => new LayoutStyle { MinWidth = noMinimum },
            "percent" => new LayoutStyle { Width = Length.Percent(0.01f), Padding = EdgeInsets<LengthPercentage>.All(LengthPercentage.Percent(0.001f)) },
            _ => new LayoutStyle(),
        };
        for (int r = 0; r < Rows; r++)
        {
            NodeHandle row = tree.CreateNode(rowStyle);
            tree.AppendChild(root, row);
            for (int c = 0; c < Columns; c++)
            {
                NodeHandle leaf = tree.CreateNode(leafStyle, static (_, _) => MeasureResult.Of(10, 10));
                tree.AppendChild(row, leaf);
                if (r == Rows / 2 && c == Columns / 2)
                {
                    target = leaf;
                }
            }
        }

        return (tree, root, target);
    }
}
