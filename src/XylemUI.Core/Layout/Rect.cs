namespace XylemUI;

/// <summary>
/// A node's border box: the position relative to its parent's border box, and the size, in logical pixels.
/// </summary>
/// <param name="X">The left edge, from the parent's left edge.</param>
/// <param name="Y">The top edge, from the parent's top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
internal readonly record struct Rect(float X, float Y, float Width, float Height);
