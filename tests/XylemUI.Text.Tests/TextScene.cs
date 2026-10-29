using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Text.Tests;

// The window of the text element tests: 400 x 300 logical pixels with the test font: its tree and frames, and what it mounted.
internal sealed class TextScene : IDisposable
{
    private readonly TextContext _text;
    private readonly GlyphAtlas _atlas = new(new TestRasterizer());
    private readonly NodeHandle _root;
    private readonly WindowFrames _frames;
    private ScopeHandle _scope;

    public TextScene(Font font)
    {
        _text = new TextContext(new OneFont(font));
        Tree = new UiTree(_text);
        _root = Tree.CreateNode(new LayoutStyle());
        _frames = new WindowFrames(Tree, _root, new NoTarget(), _atlas, Scheduler, Color.FromRgb(0xFFFFFF));
    }

    public UiTree Tree { get; }

    public Frames Scheduler { get; } = new();

    public void Mount(Func<Element> root)
    {
        UiTree.Current = Tree;
        try
        {
            _scope = Scope.Create(() => Tree.AppendChild(_root, root().Node));
        }
        finally
        {
            UiTree.Current = null;
        }

        _frames.Resize(new Size(400, 300), 1);
    }

    public TextLeaf Leaf(Element text) => Tree.Nodes.GetRef(text.Node).Text!;

    public List<GlyphInstance[]> GlyphRuns()
    {
        var runs = new List<GlyphInstance[]>();
        DisplayListReader reader = _frames.List.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            Assert.Equal(CommandTag.GlyphRun, tag);
            runs.Add(reader.ReadGlyphRun().Glyphs.ToArray());
        }

        return runs;
    }

    public void Dispose()
    {
        if (Scope.IsAlive(_scope))
        {
            Scope.Dispose(_scope);
        }

        _atlas.Dispose();
        _text.Dispose();
    }

    // Holds the one frame asked for, and runs it when asked.
    public sealed class Frames : IFrameScheduler
    {
        private Action? _callback;

        public int Requests { get; private set; }

        public void RequestFrame(Action callback)
        {
            Requests++;
            _callback = callback;
        }

        public void CancelFrame() => _callback = null;

        public void Run()
        {
            Action? callback = _callback;
            _callback = null;
            callback?.Invoke();
        }
    }
}

internal sealed class NoTarget : IRenderTarget
{
    public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas) { }

    public void Present() { }
}

internal sealed class OneFont(Font font) : IFontProvider
{
    public FontMatch ResolveFont(ReadOnlySpan<char> text, int start, TextLanguage language, in FontRequest request) =>
        new(font, text.Length - start, 1);
}
