using System.Diagnostics;
using XylemUI.Platform.Windows;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The cost of the inline objects of text leaves (row T13, design §4.8), next to the same leaves without them, with no window: a
// column of 100 leaves of one Japanese sentence at 14 px in the Windows fonts, the inline leaves with two boxes of 16 x 16 in it,
// the plain leaves with the character "□" in their places.
//
// - first_layout_*_ms: the first layout pass, which shapes every leaf and, for the inline tree, measures and places its boxes.
// - draw_*_us: a frame whose layout is clean and whose root is marked to be drawn: the draw stage of every leaf and box.
// - relayout_*_us: a frame after the column's width changes between 1,200 and 1,100, which lays every leaf out again at a width
//   its cache holds.
// - child_*_us: a frame after one leaf's first box changes its width between 16 and 18 (the plain tree: the leaf's text color
//   changes), which measures that leaf again.
// - first_box_x_inline: where the first leaf's first box sits, after "詳しくは": it shows that the boxes were placed.
//
// "inline-objects plain" or "inline-objects inline" runs one tree alone, so that the two can run in turn in processes of their own.
internal static class InlineCost
{
    private const string Head = "詳しくは";
    private const string Middle = "ヘルプを読んでください。太字と赤と下線と";
    private const string Tail = "背景のある文です。";
    private const int Leaves = 100;
    private const int Samples = 400;

    public static int Run(string? only)
    {
        UiThread.Initialize();
        using var report = new Report("inline-objects");
        using var provider = new DirectWriteFontProvider();
        using var context = new TextContext(provider);
        using var rasterizer = new DirectWriteGlyphRasterizer();
        using var atlas = new GlyphAtlas(rasterizer);
        foreach (bool inline in (ReadOnlySpan<bool>)[false, true])
        {
            if (only is not null && only != (inline ? "inline" : "plain"))
            {
                continue;
            }

            string name = inline ? "inline" : "plain";
            var tree = new UiTree(context);
            NodeHandle root = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Column, Width = Length.Px(1_200) });
            var leaves = new NodeHandle[Leaves];
            var boxes = new NodeHandle[Leaves];
            for (int i = 0; i < Leaves; i++)
            {
                leaves[i] = tree.CreateTextNode(new LayoutStyle(), new TextStyle(FontRequest.Default, 14, TextLanguage.FromTag("ja"), float.NaN));
                tree.SetTextColor(leaves[i], Color.FromRgb(0x202020));
                if (inline)
                {
                    tree.SetText(leaves[i], Head + "￼" + Middle + "￼" + Tail);
                    boxes[i] = tree.CreateNode(new LayoutStyle { Width = Length.Px(16), Height = Length.Px(16) });
                    NodeHandle second = tree.CreateNode(new LayoutStyle { Width = Length.Px(16), Height = Length.Px(16) });
                    tree.SetBackground(boxes[i], new Paint(Color.FromRgb(0x3366CC)));
                    tree.SetBackground(second, new Paint(Color.FromRgb(0xCC6633)));
                    var writer = new TextRangeWriter { Tree = tree, Owner = leaves[i] };
                    writer.Reset();
                    writer.AddInline(Head.Length, new Element(tree, boxes[i]), PlaceholderAlignment.Middle);
                    writer.AddInline(Head.Length + 1 + Middle.Length, new Element(tree, second), PlaceholderAlignment.Middle);
                    writer.Finish(Head.Length + Middle.Length + Tail.Length + 2);
                    _ = tree.Nodes.GetRef(leaves[i]).Text!.TakeRanges(writer, out _);
                    tree.AppendChild(leaves[i], boxes[i]);
                    tree.AppendChild(leaves[i], second);
                }
                else
                {
                    tree.SetText(leaves[i], Head + "□" + Middle + "□" + Tail);
                }

                tree.AppendChild(root, leaves[i]);
            }

            var scheduler = new HeldFrames();
            var frames = new WindowFrames(tree, root, new NoTarget(), atlas, scheduler, Color.FromRgb(0xFFFFFF));
            long start = Stopwatch.GetTimestamp();
            Invalidation.NotifyWindowResized(tree, root, new Size(1_200, 2_400));
            LayoutEngine.RunLayoutPass(tree);
            report.Number($"first_layout_{name}_ms", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            frames.Resize(new Size(1_200, 2_400), 1);

            var draw = new List<double>(Samples);
            var relayout = new List<double>(Samples);
            var child = new List<double>(Samples);
            for (int i = 0; i < Samples + 10; i++)
            {
                Invalidation.Invalidate(tree, root, ChangeKind.Color);
                long drawStart = Stopwatch.GetTimestamp();
                frames.RunFrame();
                double drawn = Stopwatch.GetElapsedTime(drawStart).TotalMicroseconds;

                long relayoutStart = Stopwatch.GetTimestamp();
                LayoutStyle style = tree.Nodes.GetRef(root).Layout.Style;
                tree.SetStyle(root, style with { Width = Length.Px(i % 2 == 0 ? 1_100 : 1_200) });
                frames.RunFrame();
                double relaidOut = Stopwatch.GetElapsedTime(relayoutStart).TotalMicroseconds;

                long childStart = Stopwatch.GetTimestamp();
                bool wide = (i / Leaves) % 2 == 0;
                if (inline)
                {
                    LayoutStyle box = tree.Nodes.GetRef(boxes[i % Leaves]).Layout.Style;
                    tree.SetStyle(boxes[i % Leaves], box with { Width = Length.Px(wide ? 18 : 16) });
                }
                else
                {
                    tree.SetTextColor(leaves[i % Leaves], wide ? Color.FromRgb(0xCC3333) : Color.FromRgb(0x202020));
                }

                frames.RunFrame();
                double changed = Stopwatch.GetElapsedTime(childStart).TotalMicroseconds;
                if (i >= 10)
                {
                    draw.Add(drawn);
                    relayout.Add(relaidOut);
                    child.Add(changed);
                }
            }

            report.Series($"draw_{name}_us", draw);
            report.Series($"relayout_{name}_us", relayout);
            report.Series($"child_{name}_us", child);
            report.Number($"children_{name}", inline ? 2 * Leaves : 0);
            report.Number($"first_box_x_{name}", inline ? tree.GetRect(boxes[0]).X : float.NaN);
            GC.KeepAlive(tree);
        }

        report.Number("leaves", Leaves);
        return 0;
    }

    private sealed class NoTarget : IRenderTarget
    {
        public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas) { }

        public void Present() { }
    }

    // Holds the frame asked for, and runs it when asked.
    private sealed class HeldFrames : IFrameScheduler
    {
        private Action? _callback;

        public void RequestFrame(Action callback) => _callback = callback;

        public void CancelFrame() => _callback = null;

        public void Run()
        {
            Action? callback = _callback;
            _callback = null;
            callback?.Invoke();
        }
    }
}
