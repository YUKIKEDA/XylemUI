namespace XylemUI;

/// <summary>
/// What changed on a node. The work it needs follows from it (<see cref="Invalidation.FlagsFor"/>, design §2.4).
/// </summary>
internal enum ChangeKind : byte
{
    /// <summary>The text of a text node.</summary>
    TextContent,

    /// <summary>The font of a text node.</summary>
    Font,

    /// <summary>A layout property: a size, a padding, a margin, the algorithm, a flex factor.</summary>
    Size,

    /// <summary>A color or another paint-only property.</summary>
    Color,

    /// <summary>The transform.</summary>
    TransformProp,

    /// <summary>The accessible label.</summary>
    A11yLabel,
}
