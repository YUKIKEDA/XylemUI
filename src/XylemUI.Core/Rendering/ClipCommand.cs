using System.Numerics;

namespace XylemUI;

/// <summary>
/// A clip a renderer applies per pixel, read with its <see cref="StateCommand"/>: a rounded rectangle, or a rectangle whose axes
/// are not the screen's (design §5.0, "The API of layers, clips, transforms, and DrawingContext"). A pixel's coverage is taken by
/// the distance to its edge, as a rounded rectangle's own.
/// </summary>
/// <param name="Inverse">The transform of a physical pixel's position into the clip's own space.</param>
/// <param name="X">The left edge of the clip's rectangle, in its own space.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="Radii">The radius of each corner, fitted as CSS fits them, in the clip's own space.</param>
/// <param name="Scale">The physical pixels a unit of the clip's own space spans, for the antialiasing of its edge.</param>
/// <param name="Groups">
/// The opacity groups around the clip when it was pushed. Inside a group drawn into a layer, a renderer leaves out the clips with
/// fewer groups, those around the group: it applies them once to the layer as it composites it, as they clip the group's children
/// as one.
/// </param>
public readonly record struct ClipCommand(Matrix3x2 Inverse, float X, float Y, float Width, float Height, CornerRadii Radii, float Scale, int Groups);
