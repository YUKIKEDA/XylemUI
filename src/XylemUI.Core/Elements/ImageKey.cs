namespace XylemUI;

/// <summary>
/// The key of an image in a window's cache (design §5.0.4, "Cache", "Scope and key"; IM1's Grill, Q32): the source, and the width
/// and the height the app asked the decode for.
/// </summary>
/// <remarks>
/// Two keys are equal when their sources are (<see cref="ImageSource.Equals(ImageSource?)"/>) and both sides are. A size larger
/// than the image decodes to the pixels no size does, and is still a key of its own: the image's size is not known before the
/// decode. The members are compared one by one, not through a record's generated equality, which goes through a comparer for each.
/// </remarks>
/// <param name="source">The source.</param>
/// <param name="width">The width asked for, in pixels; 0 for none.</param>
/// <param name="height">The height asked for, in pixels; 0 for none.</param>
internal readonly struct ImageKey(ImageSource source, int width, int height) : IEquatable<ImageKey>
{
    /// <summary>
    /// Gets the source.
    /// </summary>
    public ImageSource Source { get; } = source;

    /// <summary>
    /// Gets the width asked for, in pixels; 0 for none.
    /// </summary>
    public int Width { get; } = width;

    /// <summary>
    /// Gets the height asked for, in pixels; 0 for none.
    /// </summary>
    public int Height { get; } = height;

    public static bool operator ==(ImageKey left, ImageKey right) => left.Equals(right);

    public static bool operator !=(ImageKey left, ImageKey right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(ImageKey other) => Width == other.Width && Height == other.Height && Source.Equals(other.Source);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ImageKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Source, Width, Height);
}
