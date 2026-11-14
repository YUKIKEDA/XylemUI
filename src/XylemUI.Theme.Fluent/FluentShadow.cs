namespace XylemUI.Theme.Fluent;

/// <summary>
/// One of Fluent's elevation shadows (<c>shadow2</c> to <c>shadow64</c> of <see cref="FluentShadows"/>): an ambient shadow around the
/// shape and a key shadow below it, drawn together.
/// </summary>
/// <param name="Ambient">The shadow around the shape, not offset.</param>
/// <param name="Key">The shadow offset downward.</param>
public readonly record struct FluentShadow(BoxShadow Ambient, BoxShadow Key);
