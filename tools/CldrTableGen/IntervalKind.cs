namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// The ways a range of two dates is written (ICU's <c>DateIntervalFormat::formatImpl</c>).
/// </summary>
internal enum IntervalKind : byte
{
    /// <summary>
    /// The earlier date alone, with the skeleton's pattern: the dates differ only in a field smaller than the pattern's.
    /// </summary>
    Single,

    /// <summary>
    /// Both dates with the skeleton's pattern, joined by <c>intervalFormatFallback</c>.
    /// </summary>
    OwnFallback,

    /// <summary>
    /// Both dates with another pattern (yMd and the time, for a time skeleton whose dates are other days), joined by
    /// <c>intervalFormatFallback</c>.
    /// </summary>
    Fallback,

    /// <summary>
    /// An interval pattern split in two: the first part written with one date and the second with the other.
    /// </summary>
    Split,
}
