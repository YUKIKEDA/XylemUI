namespace XylemUI;

/// <summary>
/// The pipeline stages a node needs again (design §2.4). Derived from a <see cref="ChangeKind"/>, never set by callers.
/// </summary>
[Flags]
internal enum DirtyFlags : ushort
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Shaping and line breaking.</summary>
    Text = 1 << 0,

    /// <summary>Layout, and so paint.</summary>
    Layout = 1 << 1,

    /// <summary>The display list.</summary>
    Paint = 1 << 2,

    /// <summary>The hit-test data.</summary>
    HitTest = 1 << 3,

    /// <summary>The accessibility tree.</summary>
    Semantics = 1 << 4,

    /// <summary>The transform, and so paint and hit testing.</summary>
    Transform = 1 << 5,
}
