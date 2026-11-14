using System.Diagnostics;
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
internal static class A11yCost
{
    private const int Samples = 101;

    // UIA's object id of the provider of a window's root (UiaRootObjectId, UIAutomationCoreApi.h).
    private const int UiaRootObjectId = -25;

    public static int Run(int buttonCount, bool accessKit, int relations = 0)
    {
        UiThread.Initialize();
        using var report = new Report("a11y");
        report.Text("accesskit", accessKit ? "yes" : "no");
        report.Number("buttons", buttonCount);
        if (relations > 0)
        {
            report.Number("relations", relations);
        }

        using var fonts = new DirectWriteFontProvider();
        var tree = new UiTree(new TextContext(fonts));
        NodeHandle root = tree.CreateNode(new LayoutStyle());
        Signal<string> label = Signal.Create("first");
        UiTree.Current = tree;
        ScopeHandle scope;
        NodeHandle first = default;
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
                var labels = new Element[Math.Min(relations, buttonCount)];
                for (int i = 0; i < labels.Length; i++)
                {
                    labels[i] = Elements.Text($"label {i}");
                    _ = buttons[i].LabelledBy(labels[i]);
                }

                Element column = Elements.Column(buttons);
                tree.AppendChild(root, labels.Length == 0 ? column.Node : Elements.Column(column, Elements.Column(labels)).Node);
            });
        }
        finally
        {
            UiTree.Current = null;
        }

        Invalidation.NotifyWindowResized(tree, root, new Size(800, 20_000));
        LayoutEngine.RunLayoutPass(tree);
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

        report.Series("change_us", [.. change]);
        report.Series("layout_us", [.. layout]);
        report.Series("stage_us", [.. stage]);
        report.Series("idle_stage_us", [.. idle]);
        report.Series("color_stage_us", [.. color]);
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
