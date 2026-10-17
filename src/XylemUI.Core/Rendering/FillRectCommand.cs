namespace XylemUI;

/// <summary>
/// A solid rectangle read from a <see cref="DisplayList"/>, in physical pixels from the top-left corner of the target.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="Color">The color.</param>
public readonly record struct FillRectCommand(float X, float Y, float Width, float Height, Color Color);
