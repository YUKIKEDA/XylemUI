namespace XylemUI;

/// <summary>
/// The start of an opacity group, read from a <see cref="DisplayList"/> (design §5.0, "The API of layers, clips, transforms, and
/// DrawingContext"). The commands up to the matching <see cref="CommandTag.Pop"/> are its children.
/// </summary>
/// <param name="X">The left edge of the bounds of the group's children, in physical pixels.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="Alpha">The group's opacity, more than 0 and less than 1.</param>
/// <param name="Layer">
/// Whether the children are drawn into a layer and the layer is blended with <paramref name="Alpha"/>, because they overlap or
/// hold text; otherwise each child's color takes the alpha.
/// </param>
public readonly record struct GroupCommand(float X, float Y, float Width, float Height, float Alpha, bool Layer);
