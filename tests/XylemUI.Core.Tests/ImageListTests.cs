using System.Numerics;

namespace XylemUI.Core.Tests;

// What the display list writes for an image (design §5.0.4, "Passing them to the renderer"; row IM1): the rectangle on pixels as a
// solid rectangle's, the image and the part drawn, the same writing inside groups and transforms, and the values it refuses.
public sealed class ImageListTests
{
    private static readonly ImageHandle Image = new(3, 2);
    private static readonly Vector4 Part = new(2, 3, 8, 9);

    /// <summary>
    /// An image's rectangle is put on pixels as FillRect puts a rectangle's, and the command carries the image, the part drawn,
    /// and opaque white; the renderer sizes its rows by the shape count, which counts it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.25.
    ///
    /// Steps:
    /// - Fill (10.3, 4.1, 20, 16); draw the image over the same rectangle with the part (2, 3, 8, 9); read the list.
    ///
    /// Expected:
    /// - The image's rectangle is the solid rectangle's; its color is opaque white; its handle and part are the ones given; the
    ///   shape count is 1; the reader reads nothing after it.
    /// </remarks>
    [Fact]
    public void DrawImage_PutsTheRectangleOnPixels_AndCarriesTheImage()
    {
        var list = new DisplayList();
        list.Reset(1.25f);

        list.FillRect(10.3f, 4.1f, 20, 16, Color.FromRgb(0x336699));
        list.DrawImage(10.3f, 4.1f, 20, 16, Image, Part);

        DisplayListReader reader = list.GetReader();
        Assert.True(reader.TryNext(out CommandTag tag) && tag == CommandTag.FillRect);
        FillRectCommand rect = reader.ReadFillRect();
        Assert.True(reader.TryNext(out tag) && tag == CommandTag.Image);
        ImageCommand image = reader.ReadImage();
        Assert.Equal((rect.X, rect.Y, rect.Width, rect.Height), (image.X, image.Y, image.Width, image.Height));
        Assert.Equal(Color.FromRgb(0xFFFFFF), image.Color);
        Assert.Equal(Image, image.Image);
        Assert.Equal(Part, image.Source);
        Assert.Equal(1, list.ShapeCount);
        Assert.False(reader.TryNext(out _));
    }

    /// <summary>
    /// Inside a move, an image is moved as a rectangle is; inside a group, two images that overlap make the group a layer and two
    /// apart do not, as rectangles do.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Case "move": push the translation (5, 7); draw the image at (10, 10, 20, 20); fill the same; pop.
    /// - Case "groups": push opacity 0.5; draw the image at (0, 0, 20, 20) and (10, 10, 20, 20); pop; push opacity 0.5; draw it at
    ///   (0, 0, 20, 20) and (40, 0, 20, 20); pop.
    ///
    /// Expected:
    /// - "move": the image's rectangle is the solid rectangle's, (15, 17, 20, 20).
    /// - "groups": the first group is a layer and the second is not.
    /// </remarks>
    [Fact]
    public void DrawImage_InScopes_IsWrittenAsARectangle()
    {
        var moved = new DisplayList();
        moved.PushTransform(Matrix3x2.CreateTranslation(5, 7));
        moved.DrawImage(10, 10, 20, 20, Image, Part);
        moved.FillRect(10, 10, 20, 20, Color.FromRgb(0x336699));
        moved.Pop();
        DisplayListReader reader = moved.GetReader();
        ImageCommand image = default;
        FillRectCommand rect = default;
        while (reader.TryNext(out CommandTag tag))
        {
            switch (tag)
            {
                case CommandTag.Image:
                    image = reader.ReadImage();
                    break;
                case CommandTag.FillRect:
                    rect = reader.ReadFillRect();
                    break;
                case CommandTag.PushState:
                    _ = reader.ReadState(out _);
                    break;
                default:
                    reader.ReadPop();
                    break;
            }
        }

        Assert.Equal((15f, 17f, 20f, 20f), (image.X, image.Y, image.Width, image.Height));
        Assert.Equal((rect.X, rect.Y, rect.Width, rect.Height), (image.X, image.Y, image.Width, image.Height));

        var groups = new DisplayList();
        groups.PushOpacity(0.5f);
        groups.DrawImage(0, 0, 20, 20, Image, Part);
        groups.DrawImage(10, 10, 20, 20, Image, Part);
        groups.Pop();
        groups.PushOpacity(0.5f);
        groups.DrawImage(0, 0, 20, 20, Image, Part);
        groups.DrawImage(40, 0, 20, 20, Image, Part);
        groups.Pop();
        var layers = new List<bool>();
        reader = groups.GetReader();
        while (reader.TryNext(out CommandTag tag))
        {
            switch (tag)
            {
                case CommandTag.PushOpacity:
                    layers.Add(reader.ReadGroup().Layer);
                    break;
                case CommandTag.Image:
                    _ = reader.ReadImage();
                    break;
                default:
                    reader.ReadPop();
                    break;
            }
        }

        Assert.Equal([true, false], layers);
    }

    /// <summary>
    /// The default handle and a part that is not a rectangle with an area are refused, and nothing is written.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list at scale 1.
    ///
    /// Steps:
    /// - Draw with the default handle; with a part of width 0; with a part whose left is not a number; with a part of infinite
    ///   height.
    ///
    /// Expected:
    /// - ArgumentException, then ArgumentOutOfRangeException three times; the list holds no command.
    /// </remarks>
    [Fact]
    public void DrawImage_BadArguments_Throw()
    {
        var list = new DisplayList();

        _ = Assert.Throws<ArgumentException>(() => list.DrawImage(0, 0, 10, 10, default, Part));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => list.DrawImage(0, 0, 10, 10, Image, new Vector4(0, 0, 0, 4)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => list.DrawImage(0, 0, 10, 10, Image, new Vector4(float.NaN, 0, 4, 4)));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => list.DrawImage(0, 0, 10, 10, Image, new Vector4(0, 0, 4, float.PositiveInfinity)));
        Assert.Equal(0, list.CommandCount);
    }

    /// <summary>
    /// Reset forgets the images with the other commands, and a frame that draws the same images again grows no stream.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A display list that drew 40 images (past the first size of the image stream, 16) in a first frame.
    ///
    /// Steps:
    /// - Reset; read; draw the 40 images again; read.
    ///
    /// Expected:
    /// - After the reset, the list reads nothing and holds no image; after the second frame, it reads 40 images and the growth
    ///   count is the first frame's.
    /// </remarks>
    [Fact]
    public void Reset_ForgetsTheImages_AndTheNextFrameGrowsNothing()
    {
        var list = new DisplayList();
        for (int i = 0; i < 40; i++)
        {
            list.DrawImage(i, 0, 1, 1, Image, Part);
        }

        int growth = list.GrowthCount;
        list.Reset();

        Assert.False(list.GetReader().TryNext(out _));
        Assert.True(list.Images.IsEmpty);

        for (int i = 0; i < 40; i++)
        {
            list.DrawImage(i, 0, 1, 1, Image, Part);
        }

        int count = 0;
        DisplayListReader reader = list.GetReader();
        while (reader.TryNext(out _))
        {
            _ = reader.ReadImage();
            count++;
        }

        Assert.Equal(40, count);
        Assert.Equal(growth, list.GrowthCount);
    }
}
