namespace XylemUI;

/// <summary>
/// What writing a <see cref="TextStyle"/> to a text leaf changes (design §3.2, row T15).
/// </summary>
internal enum TextStyleChange : byte
{
    /// <summary>Nothing: the key and the color are those there.</summary>
    Same,

    /// <summary>The color alone: the leaf is drawn again, and its caches stay.</summary>
    PaintOnly,

    /// <summary>A field of the key: the leaf's caches are dropped and it is laid out again.</summary>
    Layout,
}
