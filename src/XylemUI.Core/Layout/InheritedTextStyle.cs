using XylemUI.Text;

namespace XylemUI;

/// <summary>
/// Some of the fields of a text style (design §3.2, "The text style a container gives"): the ones a container holds for the text
/// leaves below it, or the ones a node takes from its ancestors. <see cref="Fields"/> says which are set; the others are not read.
/// The line height is in logical pixels, as the public <c>LineHeight</c> takes it.
/// </summary>
internal struct InheritedTextStyle
{
    /// <summary>
    /// The fields set.
    /// </summary>
    public TextStyleFields Fields;

    /// <summary>
    /// The font's family; <see langword="null"/> for the OS's UI font (design §4.5).
    /// </summary>
    public string? Family;

    /// <summary>
    /// The font's weight.
    /// </summary>
    public FontWeight Weight;

    /// <summary>
    /// The font's slant.
    /// </summary>
    public FontSlant Slant;

    /// <summary>
    /// The font's stretch.
    /// </summary>
    public FontStretch Stretch;

    /// <summary>
    /// The size, in logical pixels per em.
    /// </summary>
    public float Size;

    /// <summary>
    /// The line height, in logical pixels.
    /// </summary>
    public float LineHeight;

    /// <summary>
    /// Sets the fields a nearer style sets to its values: the nearer container wins.
    /// </summary>
    /// <param name="nearer">The nearer style.</param>
    public void Override(in InheritedTextStyle nearer) => Take(in nearer, nearer.Fields);

    /// <summary>
    /// Sets the fields this style does not set and a farther one does to the farther one's values, as the walk up from a node meets
    /// its ancestors.
    /// </summary>
    /// <param name="farther">The farther style.</param>
    public void FillFrom(in InheritedTextStyle farther) => Take(in farther, farther.Fields & ~Fields);

    /// <summary>
    /// Returns whether this style sets every field another sets, each to the same value.
    /// </summary>
    /// <param name="other">The other style.</param>
    /// <returns><see langword="true"/> when writing <paramref name="other"/> over this style would change nothing.</returns>
    public readonly bool Covers(in InheritedTextStyle other)
    {
        TextStyleFields fields = other.Fields;
        return (Fields & fields) == fields
            && ((fields & TextStyleFields.Family) == 0 || string.Equals(Family, other.Family, StringComparison.Ordinal))
            && ((fields & TextStyleFields.Weight) == 0 || Weight.Value == other.Weight.Value)
            && ((fields & TextStyleFields.Slant) == 0 || Slant == other.Slant)
            && ((fields & TextStyleFields.Stretch) == 0 || Stretch == other.Stretch)
            && ((fields & TextStyleFields.Size) == 0 || Size == other.Size)
            && ((fields & TextStyleFields.LineHeight) == 0 || LineHeight == other.LineHeight);
    }

    /// <summary>
    /// Returns whether two styles set the same fields to the same values.
    /// </summary>
    /// <param name="other">The other style.</param>
    /// <returns><see langword="true"/> when they are the same.</returns>
    public readonly bool SameAs(in InheritedTextStyle other) => Fields == other.Fields && Covers(in other);

    /// <summary>
    /// Returns the key of the style a text leaf takes under this one (design §3.2, "The text style a container gives"); the color
    /// is no part of it (design §5.0, "The color of text"). Each field the leaf set itself keeps the leaf's value, each other field this
    /// style sets takes this style's, and the rest take the defaults of text (<see cref="Elements.DefaultFontSize"/>,
    /// <see cref="FontRequest.Default"/>, and the fonts' own line height). A line height in logical pixels becomes a multiple of the
    /// size the leaf takes.
    /// </summary>
    /// <param name="current">The key of the leaf's style now, which holds its own values in the fields it set and its language.</param>
    /// <param name="own">The fields the leaf set itself.</param>
    /// <param name="ownLineHeight">The line height the leaf set itself, in logical pixels; <see cref="float.NaN"/> when its own line
    /// height is the multiple in <paramref name="current"/> (a whole style written to it).</param>
    /// <returns>The key.</returns>
    public readonly TextStyleKey Resolve(in TextStyleKey current, TextStyleFields own, float ownLineHeight)
    {
        FontRequest font = current.Font;
        TextStyleFields taken = Fields & ~own;
        string? family =
            (own & TextStyleFields.Family) != 0 ? font.Family
            : (taken & TextStyleFields.Family) != 0 ? Family
            : null;
        FontWeight weight =
            (own & TextStyleFields.Weight) != 0 ? font.Weight
            : (taken & TextStyleFields.Weight) != 0 ? Weight
            : FontWeight.Normal;
        FontSlant slant =
            (own & TextStyleFields.Slant) != 0 ? font.Slant
            : (taken & TextStyleFields.Slant) != 0 ? Slant
            : FontSlant.Normal;
        FontStretch stretch =
            (own & TextStyleFields.Stretch) != 0 ? font.Stretch
            : (taken & TextStyleFields.Stretch) != 0 ? Stretch
            : FontStretch.Normal;
        float size =
            (own & TextStyleFields.Size) != 0 ? current.Size
            : (taken & TextStyleFields.Size) != 0 ? Size
            : Elements.DefaultFontSize;
        float lineHeight =
            (own & TextStyleFields.LineHeight) != 0 ? (float.IsNaN(ownLineHeight) ? current.LineHeight : ownLineHeight / size)
            : (taken & TextStyleFields.LineHeight) != 0 ? LineHeight / size
            : float.NaN;
        return new TextStyleKey(new FontRequest(family, weight, slant, stretch), size, current.Language, lineHeight);
    }

    // Copies the given fields of another style.
    private void Take(in InheritedTextStyle other, TextStyleFields fields)
    {
        if ((fields & TextStyleFields.Family) != 0)
        {
            Family = other.Family;
        }

        if ((fields & TextStyleFields.Weight) != 0)
        {
            Weight = other.Weight;
        }

        if ((fields & TextStyleFields.Slant) != 0)
        {
            Slant = other.Slant;
        }

        if ((fields & TextStyleFields.Stretch) != 0)
        {
            Stretch = other.Stretch;
        }

        if ((fields & TextStyleFields.Size) != 0)
        {
            Size = other.Size;
        }

        if ((fields & TextStyleFields.LineHeight) != 0)
        {
            LineHeight = other.LineHeight;
        }

        Fields |= fields;
    }
}
