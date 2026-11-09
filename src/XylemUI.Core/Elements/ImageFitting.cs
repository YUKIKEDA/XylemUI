using System.Numerics;

namespace XylemUI;

/// <summary>
/// Where an image is drawn in its box and which part of it (design §5.0.4, "Display"): CSS's <c>object-fit</c> with
/// <c>object-position</c> 50 % 50 % (CSS Images 3, §5.5), the image's pixels counted as logical pixels. A fit that cuts the image
/// (<see cref="ImageFit.Cover"/>, <see cref="ImageFit.None"/>) draws the part inside the box over the box, so that no clip is needed.
/// The part is kept inside the image: it is found in the box's coordinates and divided by the scale, so the rounding of a float
/// coordinate far from 0 grows by 1 / scale, and an image much larger than its box put it past the image's edge. The renderer fails
/// the invariant for a part past the edge (IM1's Grill, Q21); a large image in a small box at (1,041.56, 3,990.03) gave a part
/// 0.118 pixels past it (PR #756's review).
/// </summary>
internal static class ImageFitting
{
    /// <summary>
    /// Returns the rectangle the image is drawn over and the part of the image drawn.
    /// </summary>
    /// <param name="box">The box: left, top, width, and height, in logical pixels.</param>
    /// <param name="width">The image's width, in pixels.</param>
    /// <param name="height">The image's height, in pixels.</param>
    /// <param name="fit">How the image fills the box.</param>
    /// <param name="destination">The rectangle drawn over: left, top, width, and height; no area when nothing is drawn.</param>
    /// <returns>The part of the image drawn, in its pixels: left, top, width, and height.</returns>
    public static Vector4 Place(Vector4 box, int width, int height, ImageFit fit, out Vector4 destination)
    {
        float scale = fit switch
        {
            ImageFit.Contain => MathF.Min(box.Z / width, box.W / height),
            ImageFit.Cover => MathF.Max(box.Z / width, box.W / height),
            ImageFit.ScaleDown => MathF.Min(1, MathF.Min(box.Z / width, box.W / height)),
            ImageFit.None => 1,
            _ => float.NaN,
        };
        if (fit == ImageFit.Fill)
        {
            destination = box;
            return new Vector4(0, 0, width, height);
        }

        // The image scaled, centered in the box, then cut to the box: the part of the box it covers, and that part in the image.
        float drawnWidth = width * scale;
        float drawnHeight = height * scale;
        float left = box.X + ((box.Z - drawnWidth) / 2);
        float top = box.Y + ((box.W - drawnHeight) / 2);
        float right = MathF.Min(left + drawnWidth, box.X + box.Z);
        float bottom = MathF.Min(top + drawnHeight, box.Y + box.W);
        float cutLeft = MathF.Max(left, box.X);
        float cutTop = MathF.Max(top, box.Y);
        destination = new Vector4(cutLeft, cutTop, MathF.Max(0, right - cutLeft), MathF.Max(0, bottom - cutTop));
        if (!(scale > 0))
        {
            destination = default;
            return new Vector4(0, 0, width, height);
        }

        float partLeft = Math.Clamp((cutLeft - left) / scale, 0, width);
        float partTop = Math.Clamp((cutTop - top) / scale, 0, height);
        float partRight = Math.Clamp((right - left) / scale, partLeft, width);
        float partBottom = Math.Clamp((bottom - top) / scale, partTop, height);
        return new Vector4(partLeft, partTop, partRight - partLeft, partBottom - partTop);
    }
}
