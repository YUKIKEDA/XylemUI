namespace XylemUI;

/// <summary>
/// The kind of a command in a <see cref="DisplayList"/> (design §5.0.1). The other kinds of the design come with their rows.
/// </summary>
public enum CommandTag : byte
{
    /// <summary>A solid rectangle (<see cref="FillRectCommand"/>).</summary>
    FillRect,
}
