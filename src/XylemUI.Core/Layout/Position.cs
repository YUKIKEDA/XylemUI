namespace XylemUI;

/// <summary>
/// Whether a node takes part in its parent's layout (CSS's <c>position</c>, design §3.2, row L2).
/// </summary>
internal enum Position : byte
{
    /// <summary>In the parent's layout; its insets move it after the layout without moving the other children.</summary>
    Relative,

    /// <summary>Out of the parent's layout, placed by its insets against the parent's padding box (always the parent).</summary>
    Absolute,
}
