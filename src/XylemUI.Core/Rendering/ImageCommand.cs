using System.Numerics;

namespace XylemUI;

/// <summary>
/// An image read from a <see cref="DisplayList"/> (design §5.0.4): the part <see cref="Source"/> of the image, drawn over the
/// rectangle, in physical pixels from the top-left corner of the target.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="Color">White, with the opacity a group gave its children (row R4c) in its alpha.</param>
/// <param name="Image">The image, in the window's <see cref="TextureCache"/>.</param>
/// <param name="Source">The part of the image drawn, in the image's pixels: left, top, width, and height.</param>
internal readonly record struct ImageCommand(float X, float Y, float Width, float Height, Color Color, ImageHandle Image, Vector4 Source);
