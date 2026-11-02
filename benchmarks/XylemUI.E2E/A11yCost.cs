using System.Diagnostics;
using XylemUI.Platform.Windows;
using XylemUI.Primitives;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The accessibility tree's costs (row P02g, design §4.4.1): a column of 1,000 buttons, each with its text, in a tree with no GPU.
// The tree is made once (the first WM_GETOBJECT), then a frame changes one button's text, which marks its leaf, and the stage after
// the layout pass makes that button's node again: the change, the layout pass, and the accessibility stage are timed apart. A frame
// with no change times the stage that finds nothing to do. The leaves are measured with DirectWrite's fonts, as the todo scene's.
internal static class A11yCost
{
    private const int Buttons = 1_000;
    private const int Samples = 101;

    public static int Run()
    {
        UiThread.Initialize();
        using var report = new Report("a11y");
        using var fonts = new DirectWriteFontProvider();
        var tree = new UiTree(new TextContext(fonts));
        NodeHandle root = tree.CreateNode(new LayoutStyle());
        Signal<string> label = Signal.Create("first");
        UiTree.Current = tree;
        ScopeHandle scope;
        try
        {
            scope = Scope.Create(() =>
            {
                var buttons = new Element[Buttons];
                for (int i = 0; i < Buttons; i++)
                {
                    buttons[i] =
                        i == Buttons / 2
                            ? Primitive.Button(Elements.Text((ref text) => text.Write(label.Get())), () => { })
                            : Primitive.Button(Elements.Text($"b{i}"), () => { });
                }

                tree.AppendChild(root, Elements.Column(buttons).Node);
            });
        }
        finally
        {
            UiTree.Current = null;
        }

        Invalidation.NotifyWindowResized(tree, root, new Size(800, 20_000));
        LayoutEngine.RunLayoutPass(tree);
        long started = Stopwatch.GetTimestamp();
        tree.Accessibility.Start();
        report.Number("start_us", Stopwatch.GetElapsedTime(started).TotalMicroseconds);
        report.Number("nodes", CountNodes(tree.Accessibility.Root!));

        var change = new double[Samples];
        var layout = new double[Samples];
        var stage = new double[Samples];
        var idle = new double[Samples];
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
            if (i >= 0)
            {
                change[i] = Stopwatch.GetElapsedTime(t0, t1).TotalMicroseconds;
                layout[i] = Stopwatch.GetElapsedTime(t1, t2).TotalMicroseconds;
                stage[i] = Stopwatch.GetElapsedTime(t2, t3).TotalMicroseconds;
                idle[i] = Stopwatch.GetElapsedTime(t3, t4).TotalMicroseconds;
            }
        }

        report.Number("refreshed", tree.Accessibility.LastRefreshed);
        report.Series("change_us", [.. change]);
        report.Series("layout_us", [.. layout]);
        report.Series("stage_us", [.. stage]);
        report.Series("idle_stage_us", [.. idle]);
        Scope.Dispose(scope);
        return 0;
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
