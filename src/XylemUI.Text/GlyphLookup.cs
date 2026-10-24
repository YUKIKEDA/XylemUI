namespace XylemUI.Text;

/// <summary>
/// What <see cref="GlyphAtlas.Lookup"/> found for a glyph, without rasterizing it.
/// </summary>
public enum GlyphLookup : byte
{
    /// <summary>The glyph is in the atlas.</summary>
    Found,

    /// <summary>The glyph is not in the atlas yet; <see cref="GlyphAtlas.GetMany"/> rasterizes it.</summary>
    Missing,

    /// <summary>The atlas refused the glyph in the current frame (no room, or larger than a plot); it is not drawn this frame.</summary>
    Refused,
}
