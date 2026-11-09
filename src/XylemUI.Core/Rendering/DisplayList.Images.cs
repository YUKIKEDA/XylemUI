using System.Numerics;

namespace XylemUI;

public sealed partial class DisplayList
{
    private int _usedImages;

    /// <summary>
    /// Gets the images of the list, in the order of their commands.
    /// </summary>
    internal ReadOnlySpan<ImageRecord> Images => _shapeStreams is null ? default : _shapeStreams.Images.AsSpan(0, _usedImages);

    /// <summary>
    /// Appends an image drawn over a rectangle given in logical pixels (design §5.0.4, "Passing them to the renderer"): the part
    /// <paramref name="source"/> of the image, stretched over the rectangle, whose edges are on the nearest pixel at
    /// <see cref="Scale"/> as <see cref="FillRect(float, float, float, float, Color)"/> puts them. Inside a group, a clip, or a
    /// transform, it is written as the rectangle is.
    /// </summary>
    /// <param name="x">The left edge, in logical pixels.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="image">The image, in the window's <see cref="TextureCache"/>; it is drawn in the frame this list is drawn in.</param>
    /// <param name="source">The part of the image drawn, in the image's pixels: left, top, width, and height.</param>
    /// <exception cref="ArgumentException"><paramref name="image"/> is the handle of no image.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// An edge of <paramref name="source"/> is not a number, or its width or height is not positive.
    /// </exception>
    internal void DrawImage(float x, float y, float width, float height, ImageHandle image, Vector4 source)
    {
        if (image.IsNone)
        {
            throw new ArgumentException("The handle points to no image.", nameof(image));
        }

        if (!float.IsFinite(source.X) || !float.IsFinite(source.Y) || !(source.Z > 0) || !(source.W > 0) || !float.IsFinite(source.Z + source.W))
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "The part of the image is not a rectangle with an area.");
        }

        if (CommandCount == _commands.Length)
        {
            Grow();
        }

        float scale = Scale;
        float left = RoundToPixel(x * scale);
        float top = RoundToPixel(y * scale);
        _commands[CommandCount++] = CommandTag.Image;
        Span<float> rect = _rects.AsSpan(_usedRects, FloatsPerRect);
        rect[0] = left;
        rect[1] = top;
        rect[2] = RoundToPixel((x + width) * scale) - left;
        rect[3] = RoundToPixel((y + height) * scale) - top;
        _usedRects += FloatsPerRect;
        _colors[_usedColors++] = Opaque.Packed;
        ShapeStreams streams = _shapeStreams ??= new ShapeStreams();
        if (_usedImages == streams.Images.Length)
        {
            Array.Resize(ref streams.Images, streams.Images.Length * 2);
            GrowthCount++;
        }

        streams.Images[_usedImages++] = new ImageRecord { Image = image, Source = source };
        if (_mode != 0)
        {
            RewriteLastRect(x, y, width, height);
        }
    }
}
