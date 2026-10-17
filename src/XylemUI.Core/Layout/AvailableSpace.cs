namespace XylemUI;

/// <summary>
/// The range of sizes a parent allows a child's border box (design §2.4.1). A maximum may be infinite. Equal minimum and maximum
/// on both axes make the constraint tight: the parent decides the size, so the child's content cannot change it.
/// </summary>
/// <param name="MinWidth">The smallest width.</param>
/// <param name="MaxWidth">The largest width, or <see cref="float.PositiveInfinity"/>.</param>
/// <param name="MinHeight">The smallest height.</param>
/// <param name="MaxHeight">The largest height, or <see cref="float.PositiveInfinity"/>.</param>
internal readonly record struct AvailableSpace(float MinWidth, float MaxWidth, float MinHeight, float MaxHeight)
{
    /// <summary>
    /// Gets a value indicating whether the parent decides both sides (design §2.4, the computed boundary).
    /// </summary>
    public bool IsTight => MinWidth == MaxWidth && MinHeight == MaxHeight;

    /// <summary>
    /// Returns the tight constraint of exactly <paramref name="size"/>.
    /// </summary>
    public static AvailableSpace Definite(Size size) => new(size.Width, size.Width, size.Height, size.Height);

    /// <summary>
    /// Clamps a width into the range.
    /// </summary>
    public float ClampWidth(float width) => Math.Clamp(width, MinWidth, MaxWidth);

    /// <summary>
    /// Clamps a height into the range.
    /// </summary>
    public float ClampHeight(float height) => Math.Clamp(height, MinHeight, MaxHeight);
}
