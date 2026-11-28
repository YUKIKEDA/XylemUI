using System.Diagnostics;
using XylemUI.Platform.Windows;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The text style a container gives the text leaves below it (row TS1, design §3.2, "容器が渡す文字のスタイル", 測ること), without
// a window, with the fonts of Windows. The leaves are made as Elements.Text makes them, with no field of their own.
//   text-style [passes]: the tree of resize-text (a root column, 100 rows of 100 text leaves of a short Japanese text) laid out at
//                1,600 x 2,000. A size, then a weight, is set on the root column, each pass alternating two values, against the
//                same value set on each of the 10,000 leaves (the only way before row TS1), each case on a tree of its own: the time
//                of the setters (the walk and its marks), the time of the layout pass that follows (the leaves shaped and broken
//                again), the leaves marked and resolved by a pass, and the bytes the setters allocated over the measured passes.
//   text-style-buttons [buttons] [passes]: N button shapes in a column, each a box around a row around a text, as Fluent.Button
//                puts its content in a centering row. Each row is given the three setters Fluent.Button's effect writes when the
//                theme changes (weight, size, line height), first with the values it holds (a change of the colors alone), then
//                with other values each pass (a change of the typography): the time of the setters, the leaves resolved and
//                marked by a pass, and the layout pass after the change of the typography. The scene sets the values on the rows
//                itself, so that the executable keeps no theme's tokens (IconCost reads FluentIcons alone).
internal static class TextStyleCost
{
    private const string Word = "日本語のテキスト";
    private const int Rows = 100;
    private const int Columns = 100;
    private const int Warmup = 5;

    public static int Run(int passes)
    {
        UiThread.Initialize();
        using var report = new Report("text-style");
        using var provider = new DirectWriteFontProvider();
        using var context = new TextContext(provider);
        TextLanguage japanese = TextLanguage.FromTag("ja");

        // The clock's first read runs Stopwatch's static constructor: before any counted window (.cursor/rules/bench.mdc).
        _ = Stopwatch.GetTimestamp();
        foreach (bool size in new[] { true, false })
        {
            Measure(report, context, japanese, size, onLeaves: false, passes);
            Measure(report, context, japanese, size, onLeaves: true, passes);
        }

        report.Number("leaves", Rows * Columns);
        return 0;
    }

    public static int Buttons(int buttons, int passes)
    {
        UiThread.Initialize();
        using var report = new Report("text-style-buttons");
        using var provider = new DirectWriteFontProvider();
        using var context = new TextContext(provider);
        TextLanguage japanese = TextLanguage.FromTag("ja");
        _ = Stopwatch.GetTimestamp();

        var tree = new UiTree(context);
        NodeHandle root = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Column });
        var rows = new Element[buttons];
        var leaves = new Element[buttons];
        for (int i = 0; i < buttons; i++)
        {
            NodeHandle button = tree.CreateNode(new LayoutStyle());
            NodeHandle row = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Row });
            NodeHandle leaf = tree.CreateTextNode(new LayoutStyle(), Elements.DefaultTextStyle(japanese), TextStyleFields.None);
            tree.SetText(leaf, "Save");
            tree.AppendChild(row, leaf);
            tree.AppendChild(button, row);
            tree.AppendChild(root, button);
            rows[i] = new Element(tree, row);
            leaves[i] = new Element(tree, leaf);

            // The first run of the button's effect: the theme's Body1Strong.
            Typography(rows[i], 600, 14, 20);
        }

        Invalidation.NotifyWindowResized(tree, root, new Size(1_600, 2_000));
        LayoutEngine.RunLayoutPass(tree);

        // A change of the colors alone: the same three values again, which walk nothing.
        var sameTimes = new List<double>(passes);
        long sameResolved = 0;
        for (int pass = 0; pass < passes + Warmup; pass++)
        {
            Unmark(tree, leaves);
            long resolvedBefore = tree.TextLeavesResolved;
            long start = Stopwatch.GetTimestamp();
            foreach (Element row in rows)
            {
                Typography(row, 600, 14, 20);
            }

            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            sameResolved = tree.TextLeavesResolved - resolvedBefore;
            if (sameResolved != 0 || Marked(tree, leaves) != 0)
            {
                throw new InvalidOperationException($"The same values resolved {sameResolved} leaves; they change nothing.");
            }

            if (pass >= Warmup)
            {
                sameTimes.Add(ms);
            }
        }

        // A change of the typography: each pass writes other values, as a theme of another Body1Strong does.
        var changedTimes = new List<double>(passes);
        var layoutTimes = new List<double>(passes);
        long changedResolved = 0;
        long changedMarked = 0;
        long bytes = 0;
        for (int pass = 0; pass < passes + Warmup; pass++)
        {
            bool even = pass % 2 == 0;
            Unmark(tree, leaves);
            long resolvedBefore = tree.TextLeavesResolved;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            foreach (Element row in rows)
            {
                if (even)
                {
                    Typography(row, 700, 16, 22);
                }
                else
                {
                    Typography(row, 600, 14, 20);
                }
            }

            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            changedResolved = tree.TextLeavesResolved - resolvedBefore;
            changedMarked = Marked(tree, leaves);
            if (changedResolved != 3L * buttons || changedMarked != buttons)
            {
                throw new InvalidOperationException(
                    $"A change of the typography resolved {changedResolved} leaves and marked {changedMarked}; expected {3L * buttons} and {buttons}."
                );
            }

            long layoutStart = Stopwatch.GetTimestamp();
            LayoutEngine.RunLayoutPass(tree);
            double layoutMs = Stopwatch.GetElapsedTime(layoutStart).TotalMilliseconds;
            if (pass >= Warmup)
            {
                changedTimes.Add(ms);
                layoutTimes.Add(layoutMs);
                bytes += allocated;
            }
        }

        report.Number("buttons", buttons);
        report.Series("same_set_ms", sameTimes);
        report.Number("same_resolved", sameResolved);
        report.Series("changed_set_ms", changedTimes);
        report.Series("changed_layout_ms", layoutTimes);
        report.Number("changed_resolved", changedResolved);
        report.Number("changed_resolved_per_button", changedResolved / (double)buttons);
        report.Number("changed_marked", changedMarked);
        report.Number("changed_set_bytes", bytes);
        GC.KeepAlive(tree);
        return 0;
    }

    // One case of text-style on a tree of its own: the value set on the root column or on each leaf, then the layout pass.
    private static void Measure(Report report, TextContext context, TextLanguage language, bool size, bool onLeaves, int passes)
    {
        (UiTree tree, Element column, Element[] leaves) = Build(context, language);
        var setTimes = new List<double>(passes);
        var layoutTimes = new List<double>(passes);
        long bytes = 0;
        long marked = 0;
        long resolved = 0;
        for (int pass = 0; pass < passes + Warmup; pass++)
        {
            // Each pass changes every leaf: 16 and 14 px, or bold and regular, in turn from the default (14 px, regular).
            bool even = pass % 2 == 0;
            Unmark(tree, leaves);
            long resolvedBefore = tree.TextLeavesResolved;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            if (onLeaves)
            {
                foreach (Element leaf in leaves)
                {
                    Set(leaf, size, even);
                }
            }
            else
            {
                Set(column, size, even);
            }

            double setMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            resolved = tree.TextLeavesResolved - resolvedBefore;
            marked = Marked(tree, leaves);
            if (marked != leaves.Length)
            {
                throw new InvalidOperationException($"A pass marked {marked} of {leaves.Length} leaves; each pass changes every leaf.");
            }

            long layoutStart = Stopwatch.GetTimestamp();
            LayoutEngine.RunLayoutPass(tree);
            double layoutMs = Stopwatch.GetElapsedTime(layoutStart).TotalMilliseconds;
            if (pass >= Warmup)
            {
                setTimes.Add(setMs);
                layoutTimes.Add(layoutMs);
                bytes += allocated;
            }
        }

        string name = (onLeaves ? "leaves_" : "container_") + (size ? "size" : "weight");
        report.Series(name + "_set_ms", setTimes);
        report.Series(name + "_layout_ms", layoutTimes);
        report.Number(name + "_marked", marked);
        report.Number(name + "_resolved", resolved);
        report.Number(name + "_set_bytes", bytes);
        GC.KeepAlive(tree);
    }

    // The tree of resize-text, with leaves that take their style from their ancestors, laid out once.
    private static (UiTree Tree, Element Column, Element[] Leaves) Build(TextContext context, TextLanguage language)
    {
        var tree = new UiTree(context);
        NodeHandle root = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Column });
        var leaves = new Element[Rows * Columns];
        for (int r = 0; r < Rows; r++)
        {
            NodeHandle row = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Row });
            tree.AppendChild(root, row);
            for (int c = 0; c < Columns; c++)
            {
                NodeHandle leaf = tree.CreateTextNode(new LayoutStyle(), Elements.DefaultTextStyle(language), TextStyleFields.None);
                tree.SetText(leaf, Word);
                tree.AppendChild(row, leaf);
                leaves[(r * Columns) + c] = new Element(tree, leaf);
            }
        }

        Invalidation.NotifyWindowResized(tree, root, new Size(1_600, 2_000));
        LayoutEngine.RunLayoutPass(tree);
        return (tree, new Element(tree, root), leaves);
    }

    private static void Set(Element element, bool size, bool even)
    {
        if (size)
        {
            _ = element.FontSize(even ? 16 : 14);
        }
        else
        {
            _ = element.FontWeight(even ? FontWeight.Bold : FontWeight.Normal);
        }
    }

    // The three setters of Fluent.Button's effect, in its order.
    private static void Typography(Element row, int weight, float size, float lineHeight) =>
        _ = row.FontWeight(new FontWeight(weight)).FontSize(size).LineHeight(lineHeight);

    // Takes the text marks off the leaves, as the frame does after it shapes them (WindowFrames): the layout pass takes off only the
    // layout marks, so without this a pass would count the marks of the leaves' creation and of the passes before.
    private static void Unmark(UiTree tree, Element[] leaves)
    {
        foreach (Element leaf in leaves)
        {
            tree.Nodes.GetRef(leaf.Node).Dirty.SelfDirty &= ~DirtyFlags.Text;
        }
    }

    // Counts the leaves marked for a change of their style since the last Unmark: ChangeKind.Font sets DirtyFlags.Text.
    private static int Marked(UiTree tree, Element[] leaves)
    {
        int count = 0;
        foreach (Element leaf in leaves)
        {
            if ((tree.Nodes.GetRef(leaf.Node).Dirty.SelfDirty & DirtyFlags.Text) != 0)
            {
                count++;
            }
        }

        return count;
    }
}
