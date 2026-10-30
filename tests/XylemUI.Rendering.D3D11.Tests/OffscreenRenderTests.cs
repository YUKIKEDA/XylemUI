using XylemUI.Platform.Windows;
using XylemUI.Text;

namespace XylemUI.Rendering.D3D11.Tests;

// The renderer with no window (design §12.3, row P02a2): it draws into a texture on WARP, presents nothing, and reads the whole
// frame back. The checks of HelloWindow's --auto run here: the color of a rectangle and the ink of a line of text.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed class OffscreenRenderTests(UiThreadFixture ui)
{
    private static readonly byte[] FontBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fonts", "XylemUITestSans.ttf"));
    private static readonly TextLanguage Japanese = TextLanguage.FromTag("ja");
    private static readonly Color Clear = Color.FromRgb(0x2A2A2A);
    private static readonly Color Accent = Color.FromRgb(0x3366CC);
    private static readonly Color Light = Color.FromRgb(0xE8E8E8);

    /// <summary>
    /// An offscreen renderer is WARP with no swap chain: it draws a rectangle and a line of text into its texture, its present
    /// does nothing, and the frame read back is the size asked for, with the rectangle's color and the text's ink.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An offscreen renderer of 320 x 120, the test font, a shaper, a DirectWrite rasterizer, an atlas, and a display list.
    ///
    /// Steps:
    /// - Draw a 40 x 40 rectangle of the accent at (0, 0) and "Hello, World! 日本語" at 24 px (pen 60, baseline 30), as
    ///   HelloWindow's frame does; present; read the frame back.
    ///
    /// Expected:
    /// - The renderer is WARP, with no swap chain and no waitable object; the frame is 320 x 120; the pixel (20, 20) is the accent;
    ///   some pixel of the box of the first glyph differs from the clear color by more than 100 in red; the corner (319, 119) is
    ///   the clear color.
    /// </remarks>
    [Fact]
    public void Offscreen_DrawsIntoItsTexture_AndReadsTheFrameBack() =>
        ui.Invoke(() =>
        {
            using D3D11Renderer renderer = D3D11Renderer.CreateOffscreen(320, 120);
            using var font = new Font(FontBytes);
            using var shaper = new TextShaper();
            using var rasterizer = new DirectWriteGlyphRasterizer();
            var atlas = new GlyphAtlas(rasterizer);
            var list = new DisplayList();
            list.FillRect(0, 0, 40, 40, Accent);
            _ = list.DrawText(shaper.Shape(font, "Hello, World! 日本語", Japanese), font, 24, atlas, 60, 30, Light);
            list.ResolveGlyphs(atlas);
            GlyphInstance first = FirstGlyph(list);

            renderer.Draw(list, Clear, atlas);
            renderer.Present();
            Color[] frame = renderer.CaptureFrame();

            Assert.True(renderer.IsWarp);
            Assert.Equal(0, renderer.FrameLatencyWaitableObject);
            unsafe
            {
                Assert.True(renderer.SwapChain is null);
            }

            Assert.Equal(320 * 120, frame.Length);
            Assert.Equal(Accent, frame[(20 * 320) + 20]);
            Assert.Equal(Clear, frame[^1]);
            bool ink = false;
            for (int y = first.Y; y < first.Y + first.Height && !ink; y++)
            {
                for (int x = first.X; x < first.X + first.Width && !ink; x++)
                {
                    ink = Math.Abs(frame[(y * 320) + x].R - Clear.R) > 100;
                }
            }

            Assert.True(ink);
        });

    /// <summary>
    /// A resize makes the texture again at the new size, and the next frame fills it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An offscreen renderer of 64 x 32 that drew one frame.
    ///
    /// Steps:
    /// - Resize to 100 x 50; draw a frame with only the clear color; read it back.
    ///
    /// Expected:
    /// - The renderer is 100 x 50, and every pixel of the frame read back, 5,000, is the clear color.
    /// </remarks>
    [Fact]
    public void Offscreen_Resize_MakesTheTextureAgain() =>
        ui.Invoke(() =>
        {
            using D3D11Renderer renderer = D3D11Renderer.CreateOffscreen(64, 32);
            var list = new DisplayList();
            list.FillRect(0, 0, 64, 32, Accent);
            renderer.Draw(list, Clear, null);

            renderer.Resize(100, 50);
            list.Reset(1);
            renderer.Draw(list, Clear, null);
            Color[] frame = renderer.CaptureFrame();

            Assert.Equal((100, 50), (renderer.Width, renderer.Height));
            Assert.Equal(5_000, frame.Length);
            Assert.All(frame, color => Assert.Equal(Clear, color));
        });

    /// <summary>
    /// The read-back is not public API (design §12.3): the renderer's tests read pixels through internal members, and an app's tests
    /// through a headless app's renderer.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Look for the public instance methods of <see cref="D3D11Renderer"/> named ReadPixel, CaptureFrame, or CaptureBackBuffer.
    ///
    /// Expected:
    /// - There are none.
    /// </remarks>
    [Fact]
    public void ReadBack_IsNotPublic()
    {
        string[] names = ["ReadPixel", "CaptureFrame", "CaptureBackBuffer"];
        System.Reflection.MethodInfo[] methods = typeof(D3D11Renderer).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
        );
        Assert.DoesNotContain(methods, method => names.Contains(method.Name));
    }

    private static GlyphInstance FirstGlyph(DisplayList list)
    {
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            if (tag == CommandTag.FillRect)
            {
                _ = reader.ReadFillRect();
            }
            else
            {
                return reader.ReadGlyphRun().Glyphs[0];
            }
        }

        throw new InvalidOperationException("The list has no glyph run.");
    }
}
