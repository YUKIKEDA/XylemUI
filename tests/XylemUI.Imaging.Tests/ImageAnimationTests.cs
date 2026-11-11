using System.Text.Json;

namespace XylemUI.Imaging.Tests;

// The decoder of animated GIF and WebP images (design §5.0.4, "Animations", row IM1's 3c): the frames of make_anim.py's images
// against the frames Pillow composes (Images/README.md), their durations and plays, the shrinking, the bytes held, and what is
// refused.
public sealed class ImageAnimationTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Images");

    /// <summary>
    /// Each animation gives, play after play, the frames Pillow composes from it, with the durations the file was written with,
    /// and ends after the plays the file asks for: a GIF whose frames stay, one whose frames are cleared to the background, one
    /// with a frame that is put back to the one before, one whose first frame does not cover the canvas, and WebP animations that are lossless with alpha, lossy, and with a Display
    /// P3 profile.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The animations of Images/expected-anim.json and their frames' .bgra files (make_anim.py, Pillow 12.1.0).
    ///
    /// Steps:
    /// - Open each at its own size and read frames: every play the file asks for, or 3 plays of one with no end; then once more.
    ///
    /// Expected:
    /// - The size is the file's. Each frame of each play has the expected pixels, each byte within <paramref name="tolerance"/>
    ///   (0; 1 for the WebP with alpha, as for a still one: libwebp premultiplies with another rounding than the reference; 2
    ///   for the profile: skcms and LittleCMS compute with other precisions), and the expected duration. The read after
    ///   the last play gives the end for a file that asks for a number of plays, and the first frame again for one with no end.
    /// </remarks>
    /// <param name="name">The animation.</param>
    /// <param name="tolerance">The most a byte may differ.</param>
    [Theory]
    [InlineData("anim-keep.gif", 0)]
    [InlineData("anim-background.gif", 0)]
    [InlineData("anim-previous.gif", 0)]
    [InlineData("anim-clear-start.gif", 0)]
    [InlineData("anim-lossless.webp", 1)]
    [InlineData("anim-lossy.webp", 0)]
    [InlineData("anim-p3.webp", 2)]
    public void Next_EachFrameOfEachPlay_IsTheComposedFrame(string name, int tolerance)
    {
        JsonElement expected = Expected(name);
        int[] durations = [.. expected.GetProperty("durations").EnumerateArray().Select(d => d.GetInt32())];
        int plays = expected.GetProperty("plays").GetInt32();
        using ImageAnimation animation = ImageAnimation.TryOpen(File.ReadAllBytes(Path.Combine(Folder, name)), 0, 0, 8192)!;
        Assert.NotNull(animation);
        Assert.Equal((expected.GetProperty("width").GetInt32(), expected.GetProperty("height").GetInt32()), (animation.Width, animation.Height));
        byte[] pixels = new byte[animation.Width * animation.Height * 4];

        for (int play = 0; play < (plays == 0 ? 3 : plays); play++)
        {
            for (int frame = 0; frame < durations.Length; frame++)
            {
                Assert.Equal(AnimationStep.Frame, animation.Next(pixels, out TimeSpan duration));
                Assert.Equal(TimeSpan.FromMilliseconds(durations[frame]), duration);
                Assert.InRange(Largest(Frame(name, frame), pixels), 0, tolerance);
            }
        }

        AnimationStep after = animation.Next(pixels, out _);
        Assert.Equal(plays == 0 ? AnimationStep.Frame : AnimationStep.End, after);
        if (plays == 0)
        {
            Assert.InRange(Largest(Frame(name, 0), pixels), 0, tolerance);
        }
    }

    /// <summary>
    /// An image is an animation when it is a WebP whose header says so or a GIF with a second frame; a GIF with one frame, a still
    /// WebP, and the other formats are not, and open no decoder (design §5.0.4, "Whether it is an animation").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Images/anim-keep.gif, anim-lossy.webp, and first-frame.gif (two frames); still.gif (one frame), lossless.webp,
    ///   baseline.jpg, and no bytes.
    ///
    /// Steps:
    /// - Ask of each whether it is an animation, and open it.
    ///
    /// Expected:
    /// - <paramref name="animated"/>, and a decoder exactly for the animations.
    /// </remarks>
    /// <param name="name">The image; an empty name for no bytes.</param>
    /// <param name="animated">Whether it is an animation.</param>
    [Theory]
    [InlineData("anim-keep.gif", true)]
    [InlineData("anim-lossy.webp", true)]
    [InlineData("first-frame.gif", true)]
    [InlineData("still.gif", false)]
    [InlineData("lossless.webp", false)]
    [InlineData("baseline.jpg", false)]
    [InlineData("", false)]
    public void IsAnimation_IsTrueForAnAnimatedWebPAndAGifWithASecondFrame(string name, bool animated)
    {
        byte[] data = name.Length == 0 ? [] : File.ReadAllBytes(Path.Combine(Folder, name));

        Assert.Equal(animated, ImageAnimation.IsAnimation(data));
        using ImageAnimation? animation = ImageAnimation.TryOpen(data, 0, 0, 8192);
        Assert.Equal(animated, animation is not null);
    }

    /// <summary>
    /// An animation opened at a smaller size gives each frame composed at the image's size and then shrunk, as a still image is
    /// (design §5.0.4, "Decoding size", Q43); with a profile, the colors are converted before the shrinking (Q47).
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Images/anim-keep.gif and anim-p3.webp, 48 x 32, with their expected frames.
    ///
    /// Steps:
    /// - Open each with a width of 24 asked for, and with a longest side of 12; read the four frames.
    ///
    /// Expected:
    /// - The frames are 24 x 16, and 12 x 8. Each is the expected frame shrunk by <see cref="BoxResize"/>, each byte within
    ///   <paramref name="tolerance"/>.
    /// </remarks>
    /// <param name="name">The animation.</param>
    /// <param name="tolerance">The most a byte may differ.</param>
    [Theory]
    [InlineData("anim-keep.gif", 0)]
    [InlineData("anim-p3.webp", 2)]
    public void Next_AtASmallerSize_GivesTheComposedFrameShrunk(string name, int tolerance)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(Folder, name));
        foreach ((int targetWidth, int maxSide, int width, int height) in new[] { (24, 8192, 24, 16), (0, 12, 12, 8) })
        {
            using ImageAnimation animation = ImageAnimation.TryOpen(data, targetWidth, 0, maxSide)!;
            Assert.Equal((width, height), (animation.Width, animation.Height));
            byte[] pixels = new byte[width * height * 4];
            byte[] shrunk = new byte[width * height * 4];
            for (int frame = 0; frame < 4; frame++)
            {
                Assert.Equal(AnimationStep.Frame, animation.Next(pixels, out _));
                BoxResize.Shrink(Frame(name, frame), 48, 32, shrunk, width, height);
                Assert.InRange(Largest(shrunk, pixels), 0, tolerance);
            }
        }
    }

    /// <summary>
    /// A decoder holds its two canvases at the image's size, and one more buffer of that size when the frames are both converted
    /// from a profile and shrunk (design §5.0.4, "The bytes counted").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Images/anim-keep.gif and anim-p3.webp, 48 x 32.
    ///
    /// Steps:
    /// - Open each at its own size and with a width of 24 asked for; read the bytes held.
    ///
    /// Expected:
    /// - 48 x 32 x 4 x 2 = 12,288 for each at its own size and for the GIF at the smaller one; 18,432 for the WebP with the profile
    ///   at the smaller one.
    /// </remarks>
    [Fact]
    public void HeldBytes_AreTheCanvases_AndTheBufferOfAConvertedAndShrunkAnimation()
    {
        byte[] gif = File.ReadAllBytes(Path.Combine(Folder, "anim-keep.gif"));
        byte[] webp = File.ReadAllBytes(Path.Combine(Folder, "anim-p3.webp"));
        using ImageAnimation gifWhole = ImageAnimation.TryOpen(gif, 0, 0, 8192)!;
        using ImageAnimation gifSmall = ImageAnimation.TryOpen(gif, 24, 0, 8192)!;
        using ImageAnimation webpWhole = ImageAnimation.TryOpen(webp, 0, 0, 8192)!;
        using ImageAnimation webpSmall = ImageAnimation.TryOpen(webp, 24, 0, 8192)!;

        Assert.Equal((12288, 12288, 12288, 18432), (gifWhole.HeldBytes, gifSmall.HeldBytes, webpWhole.HeldBytes, webpSmall.HeldBytes));
    }

    /// <summary>
    /// Untrusted bytes do not throw: an animation cut short opens no decoder or gives a broken frame, and a canvas of more bytes
    /// than an array holds is not opened (design §5.0.4, "Untrusted input").
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Images/anim-keep.gif and anim-lossless.webp, each cut to its first 60 %; anim-keep.gif with its screen size changed to
    ///   65,535 x 65,535 (bytes 6 to 9), a canvas of 17,179,344,900 bytes.
    ///
    /// Steps:
    /// - Open each. Read up to 8 frames of the ones that open.
    ///
    /// Expected:
    /// - Each cut animation either opens no decoder or gives a broken frame within the 8, and no frame after it is asked for.
    ///   The large canvas opens no decoder.
    /// </remarks>
    [Fact]
    public void BrokenAndTooLargeAnimations_AreRefused()
    {
        foreach (string name in new[] { "anim-keep.gif", "anim-lossless.webp" })
        {
            byte[] whole = File.ReadAllBytes(Path.Combine(Folder, name));
            byte[] cut = whole[..(whole.Length * 6 / 10)];
            using ImageAnimation? animation = ImageAnimation.TryOpen(cut, 0, 0, 8192);
            if (animation is not null)
            {
                byte[] pixels = new byte[animation.Width * animation.Height * 4];
                AnimationStep step = AnimationStep.Frame;
                for (int i = 0; i < 8 && step == AnimationStep.Frame; i++)
                {
                    step = animation.Next(pixels, out _);
                }

                Assert.Equal(AnimationStep.Broken, step);
            }
        }

        byte[] large = File.ReadAllBytes(Path.Combine(Folder, "anim-keep.gif"));
        large.AsSpan(6, 4).Fill(0xFF);
        Assert.Null(ImageAnimation.TryOpen(large, 0, 0, 8192));
    }

    /// <summary>
    /// A disposed decoder throws when a frame is asked for, and disposing it again does nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A decoder of Images/anim-keep.gif.
    ///
    /// Steps:
    /// - Dispose it twice; ask for a frame.
    ///
    /// Expected:
    /// - The second disposal does not throw; the frame throws <see cref="ObjectDisposedException"/>.
    /// </remarks>
    [Fact]
    public void Next_AfterDispose_Throws()
    {
        ImageAnimation animation = ImageAnimation.TryOpen(File.ReadAllBytes(Path.Combine(Folder, "anim-keep.gif")), 0, 0, 8192)!;
        byte[] pixels = new byte[animation.Width * animation.Height * 4];
        animation.Dispose();
        animation.Dispose();

        _ = Assert.Throws<ObjectDisposedException>(() => animation.Next(pixels, out _));
    }

    private static JsonElement Expected(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "expected-anim.json"))).RootElement.GetProperty(name);

    private static byte[] Frame(string name, int frame) => File.ReadAllBytes(Path.Combine(Folder, $"{name}.{frame}.bgra"));

    private static int Largest(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        int largest = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            largest = Math.Max(largest, Math.Abs(expected[i] - actual[i]));
        }

        return largest;
    }
}
