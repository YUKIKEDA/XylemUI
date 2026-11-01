using System.Diagnostics;
using XylemUI.Platform.Windows;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.E2E;

// The cost of the ranges of text leaves (row T12, design §4.8), next to the same leaves without ranges, with no window: a column of
// 100 leaves of one Japanese sentence at 14 px in the Windows fonts, and the glyphs drawn by DirectWrite into the atlas.
//
// - first_layout_*_ms: the first layout pass, which splits and shapes every leaf (by range for the ranged tree).
// - draw_*_us: a frame whose layout is clean and whose root is marked to be drawn: the draw stage of every leaf, glyphs resolved.
// - restyle_*_us: a frame after one leaf changes its look: the plain leaf's color, or the ranged leaf's ranges written again with
//   the color of one range swapped (the writer, the leaf's comparison, and the frame).
// - hover_*_us: a mouse move onto the first leaf's link (or the same point of the plain leaf), then a move off it, each timed; and
//   hover_frame_*_us the frame each asks for (none for the plain leaf, whose look does not change).
//
// The ranged leaves have a link with a hovered underline, a bold range, a red range, an underlined range, and a range with a
// background. "rich-text plain" or "rich-text ranged" runs one tree alone, so that the two can run in turn in processes of their own.
internal static class RichTextCost
{
    private const string Sentence = "詳しくはヘルプを読んでください。太字と赤と下線と背景のある文です。";
    private const int Leaves = 100;
    private const int Samples = 400;

    public static int Run(string? only)
    {
        UiThread.Initialize();
        using var report = new Report("rich-text");
        using var provider = new DirectWriteFontProvider();
        using var context = new TextContext(provider);
        using var rasterizer = new DirectWriteGlyphRasterizer();
        using var atlas = new GlyphAtlas(rasterizer);

        // The fonts each tree takes, the bold one too, opened before either is timed.
        var warm = new UiTree(context);
        NodeHandle warmRoot = warm.CreateNode(new LayoutStyle());
        NodeHandle warmLeaf = warm.CreateTextNode(new LayoutStyle(), new TextStyle(FontRequest.Default, 14, TextLanguage.FromTag("ja"), float.NaN));
        warm.SetText(warmLeaf, Sentence);
        Write(warm, warmLeaf, swap: false);
        warm.AppendChild(warmRoot, warmLeaf);
        Invalidation.NotifyWindowResized(warm, warmRoot, new Size(1_200, 100));
        LayoutEngine.RunLayoutPass(warm);
        foreach (bool ranged in (ReadOnlySpan<bool>)[false, true])
        {
            if (only is not null && only != (ranged ? "ranged" : "plain"))
            {
                continue;
            }

            string name = ranged ? "ranged" : "plain";
            var tree = new UiTree(context);
            NodeHandle root = tree.CreateNode(new LayoutStyle { Algorithm = LayoutAlgorithm.Flex.Column });
            var leaves = new NodeHandle[Leaves];
            for (int i = 0; i < Leaves; i++)
            {
                leaves[i] = tree.CreateTextNode(new LayoutStyle(), new TextStyle(FontRequest.Default, 14, TextLanguage.FromTag("ja"), float.NaN));
                tree.SetText(leaves[i], Sentence);
                tree.SetTextColor(leaves[i], Color.FromRgb(0x202020));
                if (ranged)
                {
                    Write(tree, leaves[i], swap: false);
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
            var restyle = new List<double>(Samples);
            for (int i = 0; i < Samples + 10; i++)
            {
                Invalidation.Invalidate(tree, root, ChangeKind.Color);
                long drawStart = Stopwatch.GetTimestamp();
                frames.RunFrame();
                double drawn = Stopwatch.GetElapsedTime(drawStart).TotalMicroseconds;

                long restyleStart = Stopwatch.GetTimestamp();
                if (ranged)
                {
                    Write(tree, leaves[i % Leaves], swap: ((i / Leaves) % 2) == 0);
                }
                else
                {
                    tree.SetTextColor(leaves[i % Leaves], ((i / Leaves) % 2) == 0 ? Color.FromRgb(0xCC3333) : Color.FromRgb(0x202020));
                }

                frames.RunFrame();
                double restyled = Stopwatch.GetElapsedTime(restyleStart).TotalMicroseconds;
                if (i >= 10)
                {
                    draw.Add(drawn);
                    restyle.Add(restyled);
                }
            }

            report.Series($"draw_{name}_us", draw);
            report.Series($"restyle_{name}_us", restyle);

            // The link is the sentence's 5th to 7th characters ("ヘルプ"), from about 4 em in: 60 px at 14 px.
            var input = new WindowInput(tree, frames, new NoPlatform());
            var hover = new List<double>(Samples * 2);
            var hoverFrame = new List<double>(Samples * 2);
            for (int i = 0; i < Samples + 10; i++)
            {
                for (int x = 0; x < 2; x++)
                {
                    long moveStart = Stopwatch.GetTimestamp();
                    input.Handle(
                        new PointerInput(PointerInputKind.Move, x == 0 ? 62 : 300, 8, PointerButton.None, 0, 0, 0, TimeSpan.Zero, PointerType.Mouse)
                    );
                    double moved = Stopwatch.GetElapsedTime(moveStart).TotalMicroseconds;
                    long frameStart = Stopwatch.GetTimestamp();
                    scheduler.Run();
                    double framed = Stopwatch.GetElapsedTime(frameStart).TotalMicroseconds;
                    if (i >= 10)
                    {
                        hover.Add(moved);
                        hoverFrame.Add(framed);
                    }
                }
            }

            report.Series($"hover_{name}_us", hover);
            report.Series($"hover_frame_{name}_us", hoverFrame);
            report.Number($"hovered_link_{name}", tree.Nodes.GetRef(leaves[0]).Text!.Ranges?.HoveredLink ?? -2);
            GC.KeepAlive(tree);
        }

        report.Number("leaves", Leaves);
        report.Number("sentence_chars", Sentence.Length);
        report.Number("intercept_outlines", context.Intercepts.Count);
        return 0;
    }

    // The ranges of a leaf, as a builder's scopes write them: a link, a bold range, a red one (blue when swapped), an underlined
    // one, and one with a background.
    private static readonly PointerHandler Click = static (in PointerEvent _) => { };
    private static readonly SpanStyle Hovered = new(Decoration: new TextDecoration(DecorationLines.Underline));
    private static readonly SpanStyle Bold = new(Font: FontRequest.Default with { Weight = FontWeight.Bold });
    private static readonly SpanStyle Red = new(Color: Color.FromRgb(0xCC3333));
    private static readonly SpanStyle Blue = new(Color: Color.FromRgb(0x3344CC));
    private static readonly SpanStyle Underlined = new(Decoration: new TextDecoration(DecorationLines.Underline));
    private static readonly SpanStyle Shaded = new(Background: Color.FromRgb(0xFFEE88));
    private static readonly TextRangeWriter Writer = new();

    private static void Write(UiTree tree, NodeHandle leaf, bool swap)
    {
        Writer.Reset();
        Writer.Open(4, default, Writer.Ranges.AddLink(new TextLink(Click, null, null, Hovered)));
        Writer.Close(7);
        Writer.Open(16, Bold, -1);
        Writer.Close(18);
        Writer.Open(19, swap ? Blue : Red, -1);
        Writer.Close(20);
        Writer.Open(21, Underlined, -1);
        Writer.Close(23);
        Writer.Open(24, Shaded, -1);
        Writer.Close(26);
        Writer.Finish(Sentence.Length);
        if (tree.Nodes.GetRef(leaf).Text!.TakeRanges(Writer, out bool layout))
        {
            Invalidation.Invalidate(tree, leaf, layout ? ChangeKind.TextContent : ChangeKind.Color);
        }
    }

    private sealed class NoTarget : IRenderTarget
    {
        public void Draw(ReadOnlySpan<DisplayList> lists, Color clearColor, GlyphAtlas atlas) { }

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

    private sealed class NoPlatform : IPointerPlatform
    {
        public void Capture() { }

        public void ReleaseCapture() { }

        public void SetCursor(CursorKind cursor) { }
    }
}
