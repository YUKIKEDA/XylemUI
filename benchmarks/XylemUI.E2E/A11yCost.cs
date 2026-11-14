using System.Diagnostics;
using System.Numerics;
using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using XylemUI.Primitives;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The accessibility tree's costs (row P02g, design §4.4.1): a column of buttons (1,000 unless given), each with its text, in a tree
// with no GPU.
// The tree is made once (the first WM_GETOBJECT), then a frame changes one button's text, which marks its leaf, and the stage after
// the layout pass makes that button's node again: the change, the layout pass, and the accessibility stage are timed apart. A frame
// with no change times the stage that finds nothing to do, and a frame that changes the first button's color alone (a hover's look)
// times the stage after it, which has no mark. The leaves are measured with DirectWrite's fonts, as the todo scene's.
//
// With --accesskit, the tree is a hidden window's, whose UIA provider is AccessKit's adapter (Win32Accessibility): the window is
// sent the WM_GETOBJECT that UIA sends first (UiaRootObjectId), which makes the tree and hands AccessKit all of it, and the stage
// then hands AccessKit the nodes that changed. No UIA client listens, so AccessKit raises no event.
//
// With --relations N, the first N buttons are each labelled by a text of their own in a column after the buttons (row AX1, design
// §4.4.1, "Relations"), and a frame that changes the window's width, which moves every node's box and so marks every node, times the
// stage after it ("resize_stage_us"). Without the option, the scene and its output are as before.
//
// With --scroll (alone, or with --relations N), a virtualized list of 10,000 rows 20 high in a box 200 high comes after the buttons,
// and a frame scrolls it by one row, so that the list takes one row out and makes one: it times the stage after it
// ("scroll_stage_us"), counts the frames in which the list's last row is another node ("scroll_link_frames"), the nodes the last
// such stage made again ("scroll_refreshed"), and the times the measured frames made the map of the targets' ancestors again
// ("scroll_map_builds", row AX3: 0 when the rows hold no target). The rows' making and the new row's node allocate, as in an app;
// the loop itself does not.
//
// With --relations N --scroll --targets-in-rows (row AX3, PR #919's review), the targets are in the list's rows instead: row i's text
// labels button i for i < N, and there is no column of labels. Rows are keyed by their index and the scroll only goes down, so each
// measured frame makes a row that holds a new target (with N of 200 or more, every row the run makes holds one) and drops one whose
// target is freed, and Relink puts back every kept row, each holding a target: the map of the targets' ancestors is made again once
// a swap. The live targets are the rows the list keeps, about 30 ("scroll_targets_in_range"); "scroll_target_frames" counts the
// measured frames whose new last row holds a target. With --outside-targets M too, buttons N to N + M - 1 are labelled by texts in a
// column outside the list, so that each rebuild also walks M targets that no swap touches.
internal static class A11yCost
{
    private const int Samples = 101;

    // UIA's object id of the provider of a window's root (UiaRootObjectId, UIAutomationCoreApi.h).
    private const int UiaRootObjectId = -25;

    public static int Run(int buttonCount, bool accessKit, int relations = 0, bool scroll = false, bool targetsInRows = false, int outsideTargets = 0)
    {
        if (targetsInRows && (!scroll || relations == 0 || relations + outsideTargets > buttonCount))
        {
            Console.Error.WriteLine("a11y --targets-in-rows: needs --scroll and --relations N, with N plus --outside-targets at most the buttons.");
            return 1;
        }

        UiThread.Initialize();
        using var report = new Report("a11y");
        report.Text("accesskit", accessKit ? "yes" : "no");
        report.Number("buttons", buttonCount);
        if (relations > 0)
        {
            report.Number("relations", relations);
        }

        if (targetsInRows)
        {
            report.Text("targets", "rows");
            report.Number("outside_targets", outsideTargets);
        }

        using var fonts = new DirectWriteFontProvider();
        var tree = new UiTree(new TextContext(fonts));
        NodeHandle root = tree.CreateNode(new LayoutStyle());
        Signal<string> label = Signal.Create("first");
        Signal<IReadOnlyList<int>>? rowItems = scroll ? Signal.Create<IReadOnlyList<int>>([.. Enumerable.Range(0, 10_000)]) : null;
        VirtualListState? listState = scroll ? new VirtualListState() : null;
        UiTree.Current = tree;
        ScopeHandle scope;
        NodeHandle first = default;
        NodeHandle listBox = default;
        var rowTargets = new HashSet<NodeHandle>();
        try
        {
            scope = Scope.Create(() =>
            {
                var buttons = new Element[buttonCount];
                for (int i = 0; i < buttonCount; i++)
                {
                    buttons[i] =
                        i == buttonCount / 2
                            ? Primitive.Button(Elements.Text((ref text) => text.Write(label.Get())), () => { })
                            : Primitive.Button(Elements.Text($"b{i}"), () => { });
                }

                first = buttons[0].Node;

                // The labels outside the list: those of the first N buttons, or with the targets in the rows, those of the buttons
                // after the first N.
                int labelled = targetsInRows ? relations : 0;
                var labels = new Element[targetsInRows ? outsideTargets : Math.Min(relations, buttonCount)];
                for (int i = 0; i < labels.Length; i++)
                {
                    labels[i] = Elements.Text($"label {i}");
                    _ = buttons[labelled + i].LabelledBy(labels[i]);
                }

                Func<IReadOnlySignal<int>, Element> row = static _ => Elements.Box().Height(20);
                if (targetsInRows)
                {
                    row = item =>
                    {
                        // A row's item is its index and never changes.
                        int index = item.Get();
                        Element text = Elements.Text(() => $"row {item.Get()}");
                        if (index < relations)
                        {
                            _ = buttons[index].LabelledBy(text);
                            _ = rowTargets.Add(text.Node);
                        }

                        return Elements.Box(text).Height(20);
                    };
                }

                Element column = Elements.Column(buttons);
                Element content = labels.Length == 0 ? column : Elements.Column(column, Elements.Column(labels));
                if (rowItems is not null)
                {
                    Element list = Elements
                        .Box()
                        .Width(200)
                        .Height(200)
                        .Overflow(Overflow.Visible, Overflow.Scroll)
                        .VirtualFor(rowItems, static i => i, row, listState);

                    // The buttons overflow the root, and a box that scrolls has no least height of its content: without this, the
                    // column would shrink the list to nothing.
                    tree.SetStyle(list.Node, tree.Nodes.GetRef(list.Node).Layout.Style with { MinHeight = Length.Px(200) });
                    listBox = list.Node;
                    content = Elements.Column(content, list);
                }

                tree.AppendChild(root, content.Node);
            });
        }
        finally
        {
            UiTree.Current = null;
        }

        Invalidation.NotifyWindowResized(tree, root, new Size(800, 20_000));
        if (listState is not null)
        {
            // The list makes its rows in the materialization stage of a frame, which the layout pass alone does not run.
            LayOutWithRows(tree);
        }
        else
        {
            LayoutEngine.RunLayoutPass(tree);
        }

        using Win32Window? window = accessKit ? new Win32Window("a11y", 800, 600) : null;
        using Win32Accessibility? provider = window is null ? null : new Win32Accessibility(window, tree);
        if (window is not null)
        {
            window.Accessibility = provider;
        }

        long started = Stopwatch.GetTimestamp();
        if (window is not null)
        {
            _ = TerraFX.Interop.Windows.Windows.SendMessageW(window.Hwnd, WM.WM_GETOBJECT, 0, UiaRootObjectId);
            if (!tree.AccessibilityActive)
            {
                throw new InvalidOperationException("The window's WM_GETOBJECT did not make the accessibility tree.");
            }
        }
        else
        {
            tree.Accessibility.Start();
        }

        report.Number("start_us", Stopwatch.GetElapsedTime(started).TotalMicroseconds);
        report.Number("nodes", CountNodes(tree.Accessibility.Root!));

        var change = new double[Samples];
        var layout = new double[Samples];
        var stage = new double[Samples];
        var idle = new double[Samples];
        var color = new double[Samples];
        for (int i = -10; i < Samples; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            label.Set((i & 1) == 0 ? "even" : "odd");
            long t1 = Stopwatch.GetTimestamp();
            LayoutEngine.RunLayoutPass(tree);
            long t2 = Stopwatch.GetTimestamp();
            tree.Accessibility.Update();
            long t3 = Stopwatch.GetTimestamp();
            tree.Accessibility.Update();
            long t4 = Stopwatch.GetTimestamp();
            Invalidation.Invalidate(tree, first, ChangeKind.Color);
            long t5 = Stopwatch.GetTimestamp();
            tree.Accessibility.Update();
            long t6 = Stopwatch.GetTimestamp();
            if (i >= 0)
            {
                change[i] = Stopwatch.GetElapsedTime(t0, t1).TotalMicroseconds;
                layout[i] = Stopwatch.GetElapsedTime(t1, t2).TotalMicroseconds;
                stage[i] = Stopwatch.GetElapsedTime(t2, t3).TotalMicroseconds;
                idle[i] = Stopwatch.GetElapsedTime(t3, t4).TotalMicroseconds;
                color[i] = Stopwatch.GetElapsedTime(t5, t6).TotalMicroseconds;
            }
        }

        report.Number("refreshed", tree.Accessibility.LastRefreshed);
        if (relations > 0)
        {
            // A change of the window's width moves every node's box, which marks every node.
            var resize = new double[Samples];
            for (int i = -10; i < Samples; i++)
            {
                Invalidation.NotifyWindowResized(tree, root, new Size((i & 1) == 0 ? 799 : 800, 20_000));
                LayoutEngine.RunLayoutPass(tree);
                long t0 = Stopwatch.GetTimestamp();
                tree.Accessibility.Update();
                long t1 = Stopwatch.GetTimestamp();
                if (i >= 0)
                {
                    resize[i] = Stopwatch.GetElapsedTime(t0, t1).TotalMicroseconds;
                }
            }

            report.Series("resize_stage_us", [.. resize]);
        }

        if (listState is not null)
        {
            // Each frame scrolls the list by one row: one row leaves the range, one comes in, and the list links its rows again.
            var scrolled = new double[Samples];
            int linkFrames = 0;
            int targetFrames = 0;
            int mapsBefore = 0;
            float y = 0;
            for (int i = -10; i < Samples; i++)
            {
                NodeHandle lastRow = tree.Nodes.GetRef(listBox).LastChild;
                if (i == 0)
                {
                    mapsBefore = tree.Accessibility.TargetMapsMade;
                }

                y += 20;
                listState.Scroll.ScrollTo(new Vector2(0, y));
                LayOutWithRows(tree);
                long t0 = Stopwatch.GetTimestamp();
                tree.Accessibility.Update();
                long t1 = Stopwatch.GetTimestamp();
                if (i >= 0)
                {
                    scrolled[i] = Stopwatch.GetElapsedTime(t0, t1).TotalMicroseconds;
                    NodeHandle newLast = tree.Nodes.GetRef(listBox).LastChild;
                    linkFrames += newLast != lastRow ? 1 : 0;
                    targetFrames += newLast != lastRow && rowTargets.Contains(tree.Nodes.GetRef(newLast).FirstChild) ? 1 : 0;
                }
            }

            report.Series("scroll_stage_us", [.. scrolled]);
            report.Number("scroll_link_frames", linkFrames);
            report.Number("scroll_refreshed", tree.Accessibility.LastRefreshed);
            report.Number("scroll_map_builds", tree.Accessibility.TargetMapsMade - mapsBefore);
            if (targetsInRows)
            {
                int inRange = 0;
                for (NodeHandle child = tree.Nodes.GetRef(listBox).FirstChild; !child.IsNone; child = tree.Nodes.GetRef(child).NextSibling)
                {
                    inRange += rowTargets.Contains(tree.Nodes.GetRef(child).FirstChild) ? 1 : 0;
                }

                report.Number("scroll_targets_in_range", inRange);
                report.Number("scroll_target_frames", targetFrames);
            }

            // A scroll that swapped no row measured nothing of what the scene is for: it is not reported as a result.
            if (linkFrames != Samples)
            {
                Console.Error.WriteLine($"a11y --scroll: only {linkFrames} of {Samples} frames swapped a row; the scene measured nothing.");
                Scope.Dispose(scope);
                return 1;
            }
        }

        report.Series("change_us", [.. change]);
        report.Series("layout_us", [.. layout]);
        report.Series("stage_us", [.. stage]);
        report.Series("idle_stage_us", [.. idle]);
        report.Series("color_stage_us", [.. color]);
        Scope.Dispose(scope);
        return 0;
    }

    // A frame's stages that lay out a virtualized list (WindowFrames.RunFrame): the materialization stage makes the rows the range
    // needs, the layout pass lays them out, and rounds after it make the rows a range left unfilled.
    private static void LayOutWithRows(UiTree tree)
    {
        _ = tree.Materialize(0);
        LayoutEngine.RunLayoutPass(tree);
        for (int round = 1; round <= 8 && tree.Materialize(round); round++)
        {
            LayoutEngine.RunLayoutPass(tree);
        }
    }

    private static int CountNodes(AccessibilityNode node)
    {
        int count = 1;
        foreach (AccessibilityNode child in node.Children)
        {
            count += CountNodes(child);
        }

        return count;
    }
}
