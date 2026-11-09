using System.Numerics;

namespace XylemUI.Rendering.D3D11.Tests;

// Images on the D3D11 renderer (design §5.0.4, "Passing them to the renderer" and "Textures"; row IM1, 1b-1). The images are made
// in the test, uploaded through the window's texture cache, drawn on an offscreen WARP renderer, and read back: 1 to 1, a quarter
// of their size (the mipmaps), a part of them, inside a group, a rounded clip, and a rotation, and after a slot takes another image.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "Gpu")]
public sealed class ImageRenderTests(UiThreadFixture ui)
{
    private const int Width = 240;
    private const int Height = 160;
    private static readonly Color Clear = Color.FromRgb(0x2A2A2A);
    private static readonly Color Red = Color.FromRgb(0xE02020);
    private static readonly Color Green = Color.FromRgb(0x20C040);
    private static readonly Color Blue = Color.FromRgb(0x2040E0);
    private static readonly Color Yellow = Color.FromRgb(0xF0D020);

    /// <summary>
    /// An image drawn at its own size shows its pixels as they are, translucent ones over what is under them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An offscreen WARP renderer of 240 x 160, a texture cache, and a display list at scale 1.
    /// - A 16 x 12 image whose pixel (x, y) is (16x, 20y, 255 − 8x), opaque in its left half and of alpha 128 in its right half.
    ///
    /// Steps:
    /// - Draw it at (10, 10, 16, 12) with the whole image as the part; read the frame.
    ///
    /// Expected:
    /// - Each pixel of the rectangle is the image's pixel over the clear color, each channel within 1; the pixels just left of and
    ///   right of it are the clear color; one texture was uploaded.
    /// </remarks>
    [Fact]
    public void Image_AtItsOwnSize_ShowsItsPixels() =>
        ui.Invoke(() =>
        {
            static Color At(int x, int y) => new((byte)(16 * x), (byte)(20 * y), (byte)(255 - (8 * x)), x < 8 ? (byte)255 : (byte)128);
            using D3D11Renderer renderer = D3D11Renderer.CreateOffscreen(Width, Height);
            var cache = new TextureCache();
            ImageHandle image = cache.Add(16, 12, Pixels(16, 12, At));

            Color[] frame = Draw(renderer, cache, list => list.DrawImage(10, 10, 16, 12, image, new Vector4(0, 0, 16, 12)));

            for (int y = 0; y < 12; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    AssertNear(ShapeReference.Over(ShapeReference.Premultiply(At(x, y)), Clear), frame[((10 + y) * Width) + 10 + x], 1, x, y);
                }
            }

            AssertNear(Clear, frame[(15 * Width) + 9], 0, 9, 15);
            AssertNear(Clear, frame[(15 * Width) + 26], 0, 26, 15);
            Assert.Equal(1, renderer.ImageUploads);
        });

    /// <summary>
    /// An image drawn at a quarter of its size is read from its mipmaps: a checkerboard of one-pixel black and white squares shows
    /// as gray, where reading the full image between its nearest texels would show black and white.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An offscreen WARP renderer, a texture cache, and a display list at scale 1.
    /// - A 64 x 64 opaque checkerboard of one-pixel black and white squares.
    ///
    /// Steps:
    /// - Draw it at (8, 8, 16, 16); read the frame.
    ///
    /// Expected:
    /// - Each pixel of the rectangle is gray, each channel from 125 to 130 (the third mipmap is the mean of 4 x 4 texels, 127.5).
    /// </remarks>
    [Fact]
    public void Image_AtAQuarterOfItsSize_ReadsItsMipmaps() =>
        ui.Invoke(() =>
        {
            using D3D11Renderer renderer = D3D11Renderer.CreateOffscreen(Width, Height);
            var cache = new TextureCache();
            ImageHandle image = cache.Add(64, 64, Pixels(64, 64, (x, y) => ((x + y) & 1) == 0 ? Color.FromRgb(0x000000) : Color.FromRgb(0xFFFFFF)));

            Color[] frame = Draw(renderer, cache, list => list.DrawImage(8, 8, 16, 16, image, new Vector4(0, 0, 64, 64)));

            for (int y = 8; y < 24; y++)
            {
                for (int x = 8; x < 24; x++)
                {
                    Color pixel = frame[(y * Width) + x];
                    Assert.True(
                        pixel.R is >= 125 and <= 130 && pixel.G is >= 125 and <= 130 && pixel.B is >= 125 and <= 130,
                        $"pixel ({x}, {y}): {pixel}"
                    );
                }
            }
        });

    /// <summary>
    /// A part of an image shows that part only: its edges do not take the texels of the image beside it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An offscreen WARP renderer, a texture cache, and a display list at scale 1.
    /// - A 32 x 32 image of four 16 x 16 squares: red, green (top right), blue, and yellow.
    ///
    /// Steps:
    /// - Draw the part (16, 0, 16, 16) at (40, 20, 16, 16); read the frame.
    ///
    /// Expected:
    /// - Every pixel of the rectangle, its edges too, is green, each channel within 1.
    /// </remarks>
    [Fact]
    public void Image_Part_ShowsOnlyThatPart() =>
        ui.Invoke(() =>
        {
            using D3D11Renderer renderer = D3D11Renderer.CreateOffscreen(Width, Height);
            var cache = new TextureCache();
            ImageHandle image = cache.Add(32, 32, Pixels(32, 32, Quadrant));

            Color[] frame = Draw(renderer, cache, list => list.DrawImage(40, 20, 16, 16, image, new Vector4(16, 0, 16, 16)));

            for (int y = 20; y < 36; y++)
            {
                for (int x = 40; x < 56; x++)
                {
                    AssertNear(Green, frame[(y * Width) + x], 1, x, y);
                }
            }
        });

    /// <summary>
    /// An image inside a group takes the group's opacity, as a layer or as its children's alpha; inside a rounded clip it is cut by
    /// the clip; inside a rotation it turns.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An offscreen WARP renderer, a texture cache, and a display list at scale 1.
    /// - A 20 x 20 red image, a 100 x 80 blue image, and a 40 x 20 image red in its left half and blue in its right.
    ///
    /// Steps:
    /// - Case "group": push opacity 0.5; draw the red image at (20, 20) and (30, 30) (they overlap: a layer); pop; push opacity
    ///   0.5; draw it at (120, 20) and (160, 20) (apart); pop.
    /// - Case "clip": push the clip (20, 20, 60, 40) with radius 12; draw the blue image at (10, 10, 100, 80); pop.
    /// - Case "rotation": push a rotation of 90 degrees about (60, 60); draw the two-color image at (40, 50, 40, 20); pop.
    /// - Read the frame.
    ///
    /// Expected:
    /// - "group": red times 0.5 over the clear color at (25, 25), (45, 45), (125, 25), and (165, 25), within 1; one layer.
    /// - "clip": blue at (50, 40) and (22, 40); the clear color at (21, 21) and (78, 58), outside the corners' arcs.
    /// - "rotation": red at (59, 48), above the center, and blue at (59, 71), below it, within 2.
    /// </remarks>
    /// <param name="scope">The case.</param>
    [Theory]
    [InlineData("group")]
    [InlineData("clip")]
    [InlineData("rotation")]
    public void Image_InScopes_IsDrawnAsTheOtherCommands(string scope) =>
        ui.Invoke(() =>
        {
            using D3D11Renderer renderer = D3D11Renderer.CreateOffscreen(Width, Height);
            var cache = new TextureCache();
            ImageHandle red = cache.Add(20, 20, Pixels(20, 20, (_, _) => Red));
            ImageHandle blue = cache.Add(100, 80, Pixels(100, 80, (_, _) => Blue));
            ImageHandle halves = cache.Add(40, 20, Pixels(40, 20, (x, _) => x < 20 ? Red : Blue));
            var whole = new Vector4(0, 0, 20, 20);
            Color[] frame = Draw(
                renderer,
                cache,
                list =>
                {
                    switch (scope)
                    {
                        case "group":
                            list.PushOpacity(0.5f);
                            list.DrawImage(20, 20, 20, 20, red, whole);
                            list.DrawImage(30, 30, 20, 20, red, whole);
                            list.Pop();
                            list.PushOpacity(0.5f);
                            list.DrawImage(120, 20, 20, 20, red, whole);
                            list.DrawImage(160, 20, 20, 20, red, whole);
                            list.Pop();
                            break;
                        case "clip":
                            list.PushClip(20, 20, 60, 40, new CornerRadii(12));
                            list.DrawImage(10, 10, 100, 80, blue, new Vector4(0, 0, 100, 80));
                            list.Pop();
                            break;
                        default:
                            list.PushTransform(Matrix3x2.CreateRotation(MathF.PI / 2, new Vector2(60, 60)));
                            list.DrawImage(40, 50, 40, 20, halves, new Vector4(0, 0, 40, 20));
                            list.Pop();
                            break;
                    }
                }
            );

            Color Pixel(int x, int y) => frame[(y * Width) + x];
            switch (scope)
            {
                case "group":
                    Color half = ShapeReference.Over(ShapeReference.Premultiply(Red) * 0.5f, Clear);
                    AssertNear(half, Pixel(25, 25), 1, 25, 25);
                    AssertNear(half, Pixel(45, 45), 1, 45, 45);
                    AssertNear(half, Pixel(125, 25), 1, 125, 25);
                    AssertNear(half, Pixel(165, 25), 1, 165, 25);
                    Assert.Equal(1, renderer.LayerCount);
                    break;
                case "clip":
                    AssertNear(Blue, Pixel(50, 40), 1, 50, 40);
                    AssertNear(Blue, Pixel(22, 40), 1, 22, 40);
                    AssertNear(Clear, Pixel(21, 21), 0, 21, 21);
                    AssertNear(Clear, Pixel(78, 58), 0, 78, 58);
                    break;
                default:
                    AssertNear(Red, Pixel(59, 48), 2, 59, 48);
                    AssertNear(Blue, Pixel(59, 71), 2, 59, 71);
                    break;
            }
        });

    /// <summary>
    /// A released image's texture is let go before the next draw, and the slot's next image gets a texture of its own: the old
    /// handle's texture is not drawn for it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An offscreen WARP renderer, a texture cache, and a display list at scale 1.
    ///
    /// Steps:
    /// - Add a 4 x 4 red image; draw it at (10, 10). Release it; add a 4 x 4 blue image; draw that at (10, 10). Release it; draw a
    ///   frame with no image.
    ///
    /// Expected:
    /// - The first frame shows red, the second blue; the blue image has the red one's slot; the renderer held one texture after
    ///   each, uploaded two, and holds none after the last frame.
    /// </remarks>
    [Fact]
    public void Image_ReleasedSlot_TakesAnotherImage() =>
        ui.Invoke(() =>
        {
            using D3D11Renderer renderer = D3D11Renderer.CreateOffscreen(Width, Height);
            var cache = new TextureCache();
            var part = new Vector4(0, 0, 4, 4);
            ImageHandle red = cache.Add(4, 4, Pixels(4, 4, (_, _) => Red));
            AssertNear(Red, Draw(renderer, cache, list => list.DrawImage(10, 10, 4, 4, red, part))[(11 * Width) + 11], 1, 11, 11);
            Assert.Equal(1, renderer.ImageTextureCount);

            cache.Release(red);
            ImageHandle blue = cache.Add(4, 4, Pixels(4, 4, (_, _) => Blue));
            AssertNear(Blue, Draw(renderer, cache, list => list.DrawImage(10, 10, 4, 4, blue, part))[(11 * Width) + 11], 1, 11, 11);

            Assert.Equal(red.Index, blue.Index);
            Assert.Equal(1, renderer.ImageTextureCount);
            Assert.Equal(2, renderer.ImageUploads);

            cache.Release(blue);
            _ = Draw(renderer, cache, _ => { });

            Assert.Equal(0, renderer.ImageTextureCount);
        });

    /// <summary>
    /// Drawing a released image is a bug of the list's writer and fails the invariant, and a list with images drawn without the
    /// cache they are in is refused.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An offscreen WARP renderer, a texture cache with a 4 x 4 image drawn once, and a display list at scale 1.
    ///
    /// Steps:
    /// - Release the image; draw a list that draws it. Draw a list with an image and no cache.
    ///
    /// Expected:
    /// - The first draw throws InvariantViolationException (Debug builds); the second throws ArgumentException.
    /// </remarks>
    [Fact]
    public void Image_Released_FailsTheInvariant() =>
        ui.Invoke(() =>
        {
            using D3D11Renderer renderer = D3D11Renderer.CreateOffscreen(Width, Height);
            var cache = new TextureCache();
            var part = new Vector4(0, 0, 4, 4);
            ImageHandle image = cache.Add(4, 4, Pixels(4, 4, (_, _) => Red));
            _ = Draw(renderer, cache, list => list.DrawImage(10, 10, 4, 4, image, part));
            cache.Release(image);
            var list = new DisplayList();
            list.DrawImage(10, 10, 4, 4, image, part);

#if DEBUG
            _ = Assert.Throws<InvariantViolationException>(() => renderer.Draw([list], Clear, null, cache));
#endif
            _ = Assert.Throws<ArgumentException>(() => renderer.Draw([list], Clear, null, null));
        });

    /// <summary>
    /// A frame that draws images already uploaded, in a group and in a transform, allocates nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An offscreen WARP renderer of 64 x 64, a texture cache with two 8 x 8 images, and a display list.
    /// - The bytes are counted with GC.GetAllocatedBytesForCurrentThread over the frames after 20 frames of warm-up.
    ///
    /// Steps:
    /// - 100 times: reset the list; draw the two images, then both again in an opacity group of 0.5 where they overlap, then one in
    ///   a rotation; draw the list.
    ///
    /// Expected:
    /// - The 100 frames allocated 0 bytes.
    /// </remarks>
    [Fact]
    public void Image_SteadyFrames_AllocateNothing() =>
        ui.Invoke(() =>
        {
            using D3D11Renderer renderer = D3D11Renderer.CreateOffscreen(64, 64);
            var cache = new TextureCache();
            var part = new Vector4(0, 0, 8, 8);
            ImageHandle red = cache.Add(8, 8, Pixels(8, 8, (_, _) => Red));
            ImageHandle blue = cache.Add(8, 8, Pixels(8, 8, (_, _) => Blue));
            var list = new DisplayList();
            Matrix3x2 rotation = Matrix3x2.CreateRotation(0.3f, new Vector2(32, 32));
            DisplayList[] lists = [list];
            void Frame()
            {
                list.Reset();
                list.DrawImage(0, 0, 8, 8, red, part);
                list.DrawImage(10, 0, 16, 16, blue, part);
                list.PushOpacity(0.5f);
                list.DrawImage(20, 20, 8, 8, red, part);
                list.DrawImage(24, 24, 8, 8, blue, part);
                list.Pop();
                list.PushTransform(rotation);
                list.DrawImage(30, 30, 12, 12, red, part);
                list.Pop();
                renderer.Draw(lists, Clear, null, cache);
            }

            for (int i = 0; i < 20; i++)
            {
                Frame();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                Frame();
            }

            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        });

    // The four squares of a 32 x 32 image: red, green, blue, and yellow, from the top left.
    private static Color Quadrant(int x, int y) =>
        (x < 16, y < 16) switch
        {
            (true, true) => Red,
            (false, true) => Green,
            (true, false) => Blue,
            _ => Yellow,
        };

    // An image's pixels, premultiplied BGRA, from a straight color for each pixel.
    private static byte[] Pixels(int width, int height, Func<int, int, Color> at)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color = at(x, y);
                int i = ((y * width) + x) * 4;
                pixels[i] = (byte)MathF.Round(color.B * color.A / 255f);
                pixels[i + 1] = (byte)MathF.Round(color.G * color.A / 255f);
                pixels[i + 2] = (byte)MathF.Round(color.R * color.A / 255f);
                pixels[i + 3] = color.A;
            }
        }

        return pixels;
    }

    // Draws the list `write` writes, with the cache's images, over the clear color; returns the frame.
    private static Color[] Draw(D3D11Renderer renderer, TextureCache cache, Action<DisplayList> write)
    {
        var list = new DisplayList();
        write(list);
        renderer.Draw([list], Clear, null, cache);
        return renderer.CaptureFrame();
    }

    private static void AssertNear(Color expected, Color actual, int tolerance, int x, int y)
    {
        int difference = Math.Max(Math.Abs(expected.R - actual.R), Math.Max(Math.Abs(expected.G - actual.G), Math.Abs(expected.B - actual.B)));
        Assert.True(difference <= tolerance, $"pixel ({x}, {y}): expected {expected}, got {actual}");
    }
}
