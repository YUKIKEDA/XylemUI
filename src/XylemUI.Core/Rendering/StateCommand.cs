using System.Numerics;

namespace XylemUI;

/// <summary>
/// The start of a scope of a clip or a transform, read from a <see cref="DisplayList"/> (design §5.0, "The API of layers, clips,
/// transforms, and DrawingContext"): the state that the commands up to the matching <see cref="CommandTag.Pop"/> are drawn with,
/// the clips and the transform of every scope around them taken together.
/// </summary>
/// <param name="Transform">
/// The transform of the commands' positions to physical pixels. The positions are the logical ones times
/// <see cref="DisplayList.Scale"/>, before the transform; it is the identity when the state only clips.
/// </param>
/// <param name="TextScale">
/// The scale the glyph runs of the state were drawn at, the larger of the scales of <paramref name="Transform"/> along its two axes:
/// a glyph run's boxes and atlas texels are in pixels this much smaller than the positions of the other commands.
/// </param>
/// <param name="ClipRect">
/// The rectangle every pixel drawn lies in, in physical pixels: its left, top, right, and bottom edges (infinite when nothing clips
/// to a rectangle). The rectangles that clip, with no radius and axes along the screen's, are taken together into it.
/// </param>
/// <param name="Bounds">
/// The bounds of what the state lets be drawn, in physical pixels (left, top, right, and bottom): <paramref name="ClipRect"/> cut to
/// the bounds of each of the clips the renderer applies per pixel.
/// </param>
/// <param name="Index">The state's place among the list's states, for <see cref="DisplayListReader.GetState"/>.</param>
public readonly record struct StateCommand(Matrix3x2 Transform, float TextScale, Vector4 ClipRect, Vector4 Bounds, int Index)
{
    /// <summary>
    /// Gets a value indicating whether <see cref="Transform"/> only moves the commands, or leaves them where they are.
    /// </summary>
    public bool IsTranslation => Transform.M11 == 1 && Transform.M12 == 0 && Transform.M21 == 0 && Transform.M22 == 1;
}
