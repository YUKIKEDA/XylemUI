namespace XylemUI;

/// <summary>
/// A size in a style: decided by the layout (<see cref="Auto"/>) or a fixed number of logical pixels (design §3.2). Percentages
/// and content sizes come with roadmap row L1.
/// </summary>
internal readonly record struct Length
{
    private Length(bool isFixed, float pixels)
    {
        IsFixed = isFixed;
        Pixels = pixels;
    }

    /// <summary>
    /// Gets the length the layout decides.
    /// </summary>
    public static Length Auto => default;

    /// <summary>
    /// Gets a value indicating whether the length is a fixed number of pixels.
    /// </summary>
    public bool IsFixed { get; }

    /// <summary>
    /// Gets the number of pixels of a fixed length; 0 for <see cref="Auto"/>.
    /// </summary>
    public float Pixels { get; }

    /// <summary>
    /// Returns a fixed length.
    /// </summary>
    public static Length Px(float pixels) => new(true, pixels);
}
