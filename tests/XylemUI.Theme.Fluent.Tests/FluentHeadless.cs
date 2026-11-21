namespace XylemUI.Theme.Fluent.Tests;

// What the tests of the theme's runtime share: the options of a headless app wrapped by FluentRoot (design §13.2, "ヘッドレスとテスト"),
// the OS's appearances they start from, and how a node's look is read.
internal static class FluentHeadless
{
    // Eight high-contrast colors that differ from each other and from the token values, with a dark window color (5 G + 2 R + B is
    // 32, at most 1,024): the title bar of this theme is dark.
    public static readonly HighContrastColors DarkContrast = new(
        Window: Color.FromRgb(0x100000),
        WindowText: Color.FromRgb(0x200000),
        Hotlight: Color.FromRgb(0x300000),
        GrayText: Color.FromRgb(0x400000),
        Highlight: Color.FromRgb(0x500000),
        HighlightText: Color.FromRgb(0x600000),
        ButtonFace: Color.FromRgb(0x700000),
        ButtonText: Color.FromRgb(0x800000)
    );

    // The same with a light window color (5 G + 2 R + B is 1,999, above 1,024): the title bar of this theme is light.
    public static readonly HighContrastColors LightContrast = DarkContrast with { Window = Color.FromRgb(0xFFFAEF) };

    public static readonly SystemAppearance Light = new(ColorScheme.Light, false, null, null);

    public static readonly SystemAppearance Dark = new(ColorScheme.Dark, false, null, null);

    public static readonly SystemAppearance HighContrast = new(ColorScheme.Dark, true, null, DarkContrast);

    // The measured blue of Windows' accent (docs/benchmarks/th1-accent-ramp.md).
    public static readonly Color Blue = Color.FromRgb(0x0078D4);

    public static HeadlessOptions Options(SystemAppearance appearance) => new() { RootWrapper = FluentRoot.Wrap, SystemAppearance = appearance };

    public static NodeVisual Visual(HeadlessApp app, Element element) => app.Tree.Nodes.GetRef(element.Node).Visual!;

    // The window's node: the root box FluentRoot.Wrap made, the one child of the window's root node. It is read as a handle, not
    // made into an Element, since the tests read it after an await, where no function that returns an Element is called (XYLEM120).
    public static Handle<UiNode> RootBoxNode(HeadlessApp app) => app.Tree.Nodes.GetRef(app.RootNode).FirstChild;

    public static NodeVisual RootBoxVisual(HeadlessApp app) => app.Tree.Nodes.GetRef(RootBoxNode(app)).Visual!;

    public static Paint Fill(uint rgb) => new(Color.FromRgb(rgb));
}
