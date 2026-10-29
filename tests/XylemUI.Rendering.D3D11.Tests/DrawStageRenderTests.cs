using System.Numerics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using XylemUI.Platform.Windows;
using XylemUI.Text;
using NodeHandle = XylemUI.Handle<XylemUI.UiNode>;

namespace XylemUI.Rendering.D3D11.Tests;

// The draw stage against what the D3D11 renderer draws (roadmap row V1b, design §5.0, "The draw stage and the frame drive"): a tree
// drawn by WindowFrames gives the pixels of the same commands written straight into a display list at absolute positions.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed unsafe class DrawStageRenderTests(UiThreadFixture ui)
{
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));
    private static readonly TextLanguage Japanese = TextLanguage.FromTag("ja");
    private static readonly Color Clear = Color.FromRgb(0x2A2A2A);
    private static readonly Color Back = Color.FromRgb(0xE8E8E8);
    private static readonly Color Ink = Color.FromRgb(0x2040C0);
    private static readonly Color Red = Color.FromRgb(0xCC3333);
    private static readonly Color Green = Color.FromRgb(0x33AA66);

    /// <summary>
    /// A tree of boxes is drawn in tree order, each box as its shadow, its background and border, then its draw function, at its
    /// absolute position, with a hidden subtree left out: the pixels are those of the same commands written directly.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer at scale 1.25 and a window of 240 x 160 logical pixels. The root (block, padding 10) has a light background.
    ///   Its first child A (120 x 60, corner radius 8, a border of 3 in ink, a red background, a shadow offset 4 with blur 6) holds a
    ///   child B (40 x 20, green) at A's content box; A's draw function fills a 10 x 10 square in ink 5 pixels in from the bounds it
    ///   receives. The root's second child C (absolutely positioned at 100, 50, 60 x 40, ink) overlaps A. A hidden third child D
    ///   (80 x 80, red) has a green child, and a draw function that fills a red square at (150, 100) wherever its bounds are.
    ///
    /// Steps:
    /// - Run the frames' resize; draw the frame's display list and read the pixels. Write the same commands, in the same order, at
    ///   the absolute positions the layout gives, into another list; draw it and read the pixels.
    ///
    /// Expected:
    /// - The two images are the same, pixel for pixel; A's draw function received A's absolute bounds (10, 10, 120, 60), and D's
    ///   was not called.
    /// </remarks>
    [Fact]
    public void Tree_DrawsAsTheSameCommandsWrittenDirectly() =>
        ui.Invoke(() =>
        {
            const float Scale = 1.25f;
            using var window = new Win32Window("draw stage", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var rasterizer = new DirectWriteGlyphRasterizer();
            using var atlas = new GlyphAtlas(rasterizer);
            var shadow = new BoxShadow(Color.FromRgb(0x000000) with { A = 128 }, new Vector2(4, 4), 6);

            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle { Padding = EdgeInsets.All(10) });
            tree.SetBackground(root, new Paint(Back));
            NodeHandle a = Child(
                tree,
                root,
                new LayoutStyle
                {
                    Width = Length.Px(120),
                    Height = Length.Px(60),
                    Border = EdgeInsets.All(3),
                }
            );
            tree.SetBackground(a, new Paint(Red));
            tree.SetCornerRadii(a, new CornerRadii(8));
            tree.SetBorderPaint(a, new Paint(Ink));
            tree.SetShadow(a, shadow);
            Rect received = default;
            tree.SetDraw(
                a,
                (context, bounds) =>
                {
                    received = bounds;
                    context.FillRect(bounds.X + 5, bounds.Y + 5, 10, 10, Ink);
                }
            );
            NodeHandle b = Child(tree, a, new LayoutStyle { Width = Length.Px(40), Height = Length.Px(20) });
            tree.SetBackground(b, new Paint(Green));
            NodeHandle c = Child(
                tree,
                root,
                new LayoutStyle
                {
                    Position = Position.Absolute,
                    Inset = new EdgeInsets<LengthPercentageAuto>(
                        LengthPercentageAuto.Px(100),
                        LengthPercentageAuto.Px(50),
                        LengthPercentageAuto.Auto,
                        LengthPercentageAuto.Auto
                    ),
                    Width = Length.Px(60),
                    Height = Length.Px(40),
                }
            );
            tree.SetBackground(c, new Paint(Ink));
            NodeHandle d = Child(
                tree,
                root,
                new LayoutStyle
                {
                    Width = Length.Px(80),
                    Height = Length.Px(80),
                    Hidden = true,
                }
            );
            tree.SetBackground(d, new Paint(Red));
            bool hiddenDrawn = false;
            tree.SetDraw(
                d,
                (context, _) =>
                {
                    hiddenDrawn = true;
                    context.FillRect(150, 100, 30, 30, Red);
                }
            );
            NodeHandle e = Child(tree, d, new LayoutStyle { Width = Length.Px(20), Height = Length.Px(20) });
            tree.SetBackground(e, new Paint(Green));

            var frames = new WindowFrames(tree, root, new NoTarget(), atlas, new NoScheduler(), Clear);
            frames.Resize(new Size(width / Scale, height / Scale), Scale);
            Color[] viaTree = Draw(renderer, frames.List, width, height, atlas);

            var direct = new DisplayList();
            direct.Reset(Scale);
            direct.FillRect(0, 0, width / Scale, height / Scale, new Paint(Back), default);
            direct.DrawShadow(10, 10, 120, 60, new CornerRadii(8), shadow);
            direct.FillRect(10, 10, 120, 60, new Paint(Red), new CornerRadii(8), new Border(new BorderWidths(3, 3, 3, 3), new Paint(Ink)));
            direct.FillRect(15, 15, 10, 10, Ink);
            direct.FillRect(13, 13, 40, 20, new Paint(Green), default);
            direct.FillRect(100, 50, 60, 40, new Paint(Ink), default);
            direct.ResolveGlyphs(atlas);
            Color[] written = Draw(renderer, direct, width, height, atlas);

            Assert.Equal(new Rect(10, 10, 120, 60), received);
            Assert.False(hiddenDrawn);
            Assert.Equal(written, viaTree);
        });

    /// <summary>
    /// A text leaf draws its committed lines at its content box, each glyph where the display list's DrawText puts the same shaped
    /// glyphs: the glyphs and the pixels are the same, smoothed and bilevel (where the OS does not smooth fonts, row T8).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - As above; the root (padding 10) holds a text leaf (padding 5) of "nn日nn" at 20 px in the test font, in ink. The test
    ///   font has an ascent of 1160 in 1000 units per em (fontTools), so the first baseline is 23.2 below the content box's top.
    ///
    /// Steps:
    /// - Set the frames' text rendering bilevel or not; run the frames' resize; read the glyphs and pixels of the frame's list.
    ///   Shape the text directly and DrawText it at (15, 15 + 23.2) into another list, bilevel or not alike; read its glyphs and
    ///   pixels.
    ///
    /// Expected:
    /// - The same glyphs, and the same pixels.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextLeaf_DrawsItsLinesAsDrawText(bool aliased) =>
        ui.Invoke(() =>
        {
            const float Scale = 1.25f;
            using var window = new Win32Window("draw stage text", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var rasterizer = new DirectWriteGlyphRasterizer();
            using var atlas = new GlyphAtlas(rasterizer);
            using var font = new Font(FontBytes);
            using var context = new TextContext(new OneFont(font));
            const string Text = "nn日nn";

            var tree = new UiTree(context);
            NodeHandle root = tree.CreateNode(new LayoutStyle { Padding = EdgeInsets.All(10) });
            NodeHandle leaf = tree.CreateTextNode(
                new LayoutStyle { Padding = EdgeInsets.All(5) },
                new TextStyle(FontRequest.Default, 20, Japanese, float.NaN)
            );
            tree.SetText(leaf, Text);
            tree.SetTextColor(leaf, Ink);
            tree.AppendChild(root, leaf);
            var frames = new WindowFrames(tree, root, new NoTarget(), atlas, new NoScheduler(), Clear);
            frames.TextRendering = frames.TextRendering with { IsAliased = aliased };
            frames.Resize(new Size(width / Scale, height / Scale), Scale);
            List<string> treeGlyphs = Glyphs(frames.List);
            Color[] viaTree = Draw(renderer, frames.List, width, height, atlas);

            var direct = new DisplayList();
            direct.Reset(Scale);
            _ = direct.DrawText(context.Shaper.Shape(font, Text, Japanese), font, 20, atlas, 15, 15 + 23.2f, Ink, aliased: aliased);
            direct.ResolveGlyphs(atlas);
            List<string> directGlyphs = Glyphs(direct);
            Color[] written = Draw(renderer, direct, width, height, atlas);

            Assert.Equal(directGlyphs, treeGlyphs);
            Assert.Equal(written, viaTree);
        });

    /// <summary>
    /// A window's size message runs a frame at once with the new size, through WindowFrames.Resize: the synchronous resize path
    /// (design §2.11.1) is the same frame.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window of 300 x 200 with a WARP renderer, and frames of a tree whose root has a background. The window's
    ///   synchronous render resizes the renderer and calls WindowFrames.Resize with the client size in logical pixels (scale 1).
    ///
    /// Steps:
    /// - Resize the window to 420 x 310 with SetWindowPos, which sends WM_SIZE before it returns; no message loop runs.
    ///
    /// Expected:
    /// - Before SetWindowPos returned, one more frame presented, and the root was laid out at the new client size.
    /// </remarks>
    [Fact]
    public void SizeMessage_RunsAFrameAtTheNewSize() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("draw stage resize", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var rasterizer = new DirectWriteGlyphRasterizer();
            using var atlas = new GlyphAtlas(rasterizer);
            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            tree.SetBackground(root, new Paint(Back));
            var frames = new WindowFrames(tree, root, renderer, atlas, new NoScheduler(), Clear);
            frames.Resize(new Size(width, height), 1);
            window.SynchronousRender = (_, w, h) =>
            {
                renderer.Resize(w, h);
                frames.Resize(new Size(w, h), 1);
            };
            int presents = frames.PresentCount;

            const uint Flags = (uint)(SWP.SWP_NOMOVE | SWP.SWP_NOZORDER | SWP.SWP_NOACTIVATE);
            Assert.True(TerraFX.Interop.Windows.Windows.SetWindowPos(window.Hwnd, HWND.NULL, 0, 0, 420, 310, Flags));

            (int newWidth, int newHeight) = window.ClientSize;
            Assert.Equal(presents + 1, frames.PresentCount);
            Assert.Equal(new Rect(0, 0, newWidth, newHeight), tree.GetRect(root));
        });

    /// <summary>
    /// A node's pseudo-states lay their styles over its look, the pressed one over the hovered one, and a text leaf with no color of
    /// its own draws in the color its ancestors resolve: the pixels are those of the resolved commands written directly (row V1c,
    /// design §5.0, "Opacity, transforms, and pseudo-states").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer at scale 1.25. A button box (absolutely at 20, 20, 120 x 40, border 2, corner radius 6) has a gray
    ///   background, an ink border, and the ink text color; its hovered style sets a red background, a green border, and a white
    ///   text color; its pressed style a green background. It holds a text leaf of "nn" at 20 px with no color.
    ///
    /// Steps:
    /// - Set the button's states (none, hovered, pressed, both); run the frames' resize; read the pixels. Write the background and
    ///   border the states resolve, and the text in the color they resolve at the leaf's content box (22, 22 + 23.2), into another
    ///   list; read its pixels.
    ///
    /// Expected:
    /// - The same pixels: none gray, ink, ink; hovered red, green, white; pressed green, ink, ink; both green, green, white.
    /// </remarks>
    /// <param name="states">The pseudo-states, as the bits of <c>PseudoState</c>: 1 hovered, 2 pressed.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PseudoStates_LayTheirStylesOver_AsTheCommandsWrittenDirectly(int states) =>
        ui.Invoke(() =>
        {
            const float Scale = 1.25f;
            Color white = Color.FromRgb(0xFFFFFF);
            Color gray = Color.FromRgb(0x808080);
            using var window = new Win32Window("pseudo-states", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var rasterizer = new DirectWriteGlyphRasterizer();
            using var atlas = new GlyphAtlas(rasterizer);
            using var font = new Font(FontBytes);
            using var context = new TextContext(new OneFont(font));

            var tree = new UiTree(context);
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle button = Child(tree, root, At(20, 20, 120, 40) with { Border = EdgeInsets.All(2) });
            tree.SetBackground(button, new Paint(gray));
            tree.SetBorderPaint(button, new Paint(Ink));
            tree.SetCornerRadii(button, new CornerRadii(6));
            tree.SetForeground(button, Ink);
            tree.SetHoveredStyle(
                button,
                new PaintStyle
                {
                    Background = Red,
                    BorderPaint = Green,
                    Foreground = white,
                }
            );
            tree.SetPressedStyle(button, new PaintStyle { Background = Green });
            NodeHandle leaf = tree.CreateTextNode(new LayoutStyle(), new TextStyle(FontRequest.Default, 20, Japanese, float.NaN));
            tree.SetText(leaf, "nn");
            tree.AppendChild(button, leaf);
            tree.SetPseudoState(button, PseudoState.Hovered, (states & 1) != 0);
            tree.SetPseudoState(button, PseudoState.Pressed, (states & 2) != 0);
            var frames = new WindowFrames(tree, root, new NoTarget(), atlas, new NoScheduler(), Clear);
            frames.TextRendering = frames.TextRendering with { Coverage = GlyphCoverage.Gray };
            frames.Resize(new Size(width / Scale, height / Scale), Scale);
            Color[] viaTree = Draw(renderer, frames.List, width, height, atlas);

            bool hovered = (states & 1) != 0;
            bool pressed = (states & 2) != 0;
            Color background =
                pressed ? Green
                : hovered ? Red
                : gray;
            Color border = hovered ? Green : Ink;
            Color text = hovered ? white : Ink;
            var direct = new DisplayList();
            direct.Reset(Scale);
            direct.FillRect(20, 20, 120, 40, new Paint(background), new CornerRadii(6), new Border(new BorderWidths(2, 2, 2, 2), new Paint(border)));
            _ = direct.DrawText(context.Shaper.Shape(font, "nn", Japanese), font, 20, atlas, 22, 22 + 23.2f, text);
            direct.ResolveGlyphs(atlas);
            Color[] written = Draw(renderer, direct, width, height, atlas);

            Assert.Equal(written, viaTree);
        });

    /// <summary>
    /// A node's transform turns and scales its subtree about the center of its border box, and its opacity draws its subtree as one
    /// group: the pixels are those of the same scopes written directly around the same commands.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A WARP renderer at scale 1.25. A red box (absolutely at 40, 30, 60 x 40) turned 30° and scaled 1.5 about its center
    ///   (70, 50) holds a green child (20 x 20 at its top left). An ink box (absolutely at 140, 20, 50 x 50) of opacity 0.5 holds a
    ///   red child (30 x 30 at its top left) that overlaps it.
    ///
    /// Steps:
    /// - Run the frames' resize; read the pixels. Write into another list: a transform of the turn and scale about (70, 50) around
    ///   the red box and its child; an opacity group of 0.5 around the ink box and its child; read its pixels.
    ///
    /// Expected:
    /// - The same pixels.
    /// </remarks>
    [Fact]
    public void TransformAndOpacity_DrawAsTheScopesWrittenDirectly() =>
        ui.Invoke(() =>
        {
            const float Scale = 1.25f;
            using var window = new Win32Window("transform and opacity", 300, 200);
            (int width, int height) = window.ClientSize;
            using var renderer = D3D11Renderer.Create(window.Handle, width, height, useWarp: true);
            using var rasterizer = new DirectWriteGlyphRasterizer();
            using var atlas = new GlyphAtlas(rasterizer);
            Matrix3x2 turn = Matrix3x2.CreateRotation(MathF.PI / 6) * Matrix3x2.CreateScale(1.5f);

            var tree = new UiTree();
            NodeHandle root = tree.CreateNode(new LayoutStyle());
            NodeHandle turned = Child(tree, root, At(40, 30, 60, 40));
            tree.SetBackground(turned, new Paint(Red));
            tree.SetTransform(turned, turn);
            NodeHandle inner = Child(tree, turned, new LayoutStyle { Width = Length.Px(20), Height = Length.Px(20) });
            tree.SetBackground(inner, new Paint(Green));
            NodeHandle faded = Child(tree, root, At(140, 20, 50, 50));
            tree.SetBackground(faded, new Paint(Ink));
            tree.SetOpacity(faded, 0.5f);
            NodeHandle overlap = Child(tree, faded, new LayoutStyle { Width = Length.Px(30), Height = Length.Px(30) });
            tree.SetBackground(overlap, new Paint(Red));
            var frames = new WindowFrames(tree, root, new NoTarget(), atlas, new NoScheduler(), Clear);
            frames.Resize(new Size(width / Scale, height / Scale), Scale);
            Color[] viaTree = Draw(renderer, frames.List, width, height, atlas);

            var direct = new DisplayList();
            direct.Reset(Scale);
            direct.PushTransform(Matrix3x2.CreateTranslation(-70, -50) * turn * Matrix3x2.CreateTranslation(70, 50));
            direct.FillRect(40, 30, 60, 40, new Paint(Red), default);
            direct.FillRect(40, 30, 20, 20, new Paint(Green), default);
            direct.Pop();
            direct.PushOpacity(0.5f);
            direct.FillRect(140, 20, 50, 50, new Paint(Ink), default);
            direct.FillRect(140, 20, 30, 30, new Paint(Red), default);
            direct.Pop();
            direct.ResolveGlyphs(atlas);
            Color[] written = Draw(renderer, direct, width, height, atlas);

            Assert.Equal(written, viaTree);
        });

    private static LayoutStyle At(float x, float y, float width, float height) =>
        new()
        {
            Position = Position.Absolute,
            Inset = new EdgeInsets<LengthPercentageAuto>(
                LengthPercentageAuto.Px(x),
                LengthPercentageAuto.Px(y),
                LengthPercentageAuto.Auto,
                LengthPercentageAuto.Auto
            ),
            Width = Length.Px(width),
            Height = Length.Px(height),
        };

    private static NodeHandle Child(UiTree tree, NodeHandle parent, LayoutStyle style)
    {
        NodeHandle child = tree.CreateNode(style);
        tree.AppendChild(parent, child);
        return child;
    }

    private static Color[] Draw(D3D11Renderer renderer, DisplayList list, int width, int height, GlyphAtlas atlas)
    {
        renderer.Draw(list, Clear, atlas);
        ID3D11Texture2D* capture = renderer.CaptureBackBuffer();
        try
        {
            return renderer.ReadCapturedPixels(capture, width, height);
        }
        finally
        {
            _ = capture->Release();
        }
    }

    // Each glyph run's glyphs and color, in order: the lists of the text test hold glyph runs only.
    private static List<string> Glyphs(DisplayList list)
    {
        var runs = new List<string>();
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            Assert.Equal(CommandTag.GlyphRun, tag);
            GlyphRunCommand run = reader.ReadGlyphRun();
            runs.Add($"{run.Color} {string.Join(';', run.Glyphs.ToArray())}");
        }

        return runs;
    }

    // The frames here are drawn by the test, which reads the back buffer before any present.
    private sealed class NoTarget : IRenderTarget
    {
        public void Draw(DisplayList list, Color clearColor, GlyphAtlas atlas) { }

        public void Present() { }
    }

    private sealed class NoScheduler : IFrameScheduler
    {
        public void RequestFrame(Action callback) { }

        public void CancelFrame() { }
    }

    private sealed class OneFont(Font font) : IFontProvider
    {
        public FontMatch ResolveFont(ReadOnlySpan<char> text, int start, TextLanguage language, in FontRequest request) =>
            new(font, text.Length - start, 1);
    }
}
