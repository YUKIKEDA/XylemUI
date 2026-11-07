using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// The size of a grid's track (design §3.3, CSS Grid §7.2): a minimum and a maximum track sizing function. Make one with
/// <see cref="Track"/>.
/// </summary>
public readonly record struct TrackSize
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrackSize"/> struct from its two sizing functions.
    /// </summary>
    /// <param name="min">The minimum track sizing function.</param>
    /// <param name="max">The maximum track sizing function.</param>
    internal TrackSize(TrackFunction min, TrackFunction max)
    {
        Min = min;
        Max = max;
    }

    /// <summary>
    /// Gets the minimum track sizing function.
    /// </summary>
    internal TrackFunction Min { get; }

    /// <summary>
    /// Gets the maximum track sizing function.
    /// </summary>
    internal TrackFunction Max { get; }

    /// <summary>
    /// Returns the track of one sizing function: a length, a percentage, <c>auto</c>, <c>min-content</c>, or <c>max-content</c>
    /// for both ends; <c>fr</c> and <c>fit-content()</c> with <c>auto</c> as the minimum (CSS Grid §7.2.1).
    /// </summary>
    /// <param name="function">The sizing function.</param>
    /// <returns>The track size.</returns>
    internal static TrackSize Of(TrackFunction function) =>
        function.Kind is TrackKind.Fr or TrackKind.FitContentPx or TrackKind.FitContentPercent
            ? new TrackSize(TrackFunction.Auto, function)
            : new TrackSize(function, function);

    /// <summary>
    /// Gets a value indicating whether either sizing function is a length or a percentage, as every track of a template with an
    /// automatic repetition must have (CSS Grid §7.2.3.2; Taffy's <c>has_fixed_component</c>).
    /// </summary>
    internal bool HasFixedComponent => Min.Kind is TrackKind.Px or TrackKind.Percent || Max.Kind is TrackKind.Px or TrackKind.Percent;

    /// <summary>
    /// Returns the size an automatic repetition counts the track as: its maximum where that is fixed, floored by a fixed
    /// minimum, else its minimum (CSS Grid §7.2.3.2; Taffy's <c>track_definite_value</c>).
    /// </summary>
    /// <param name="basis">The content box's size in the axis.</param>
    /// <returns>The pixels.</returns>
    internal float RepetitionSize(float basis)
    {
        float max = Max.DefiniteValue(basis);
        float min = Min.DefiniteValue(basis);
        return float.IsNaN(max) ? min : Maybe.Max(max, min);
    }
}

/// <summary>
/// How many times an automatic repetition repeats its tracks (design §3.3, CSS Grid §7.2.3.2): as many as fit in the grid
/// container.
/// </summary>
public enum Repeat : byte
{
    /// <summary>
    /// Repeats as many times as fit, and keeps the tracks that no item takes (<c>auto-fill</c>).
    /// </summary>
    AutoFill = 1,

    /// <summary>
    /// Repeats as many times as fit, and collapses the tracks that no item takes (<c>auto-fit</c>).
    /// </summary>
    AutoFit = 2,
}

/// <summary>
/// One entry of a grid's track list (design §3.3, CSS Grid §7.2): a track, or a track list repeated a number of times
/// (<c>repeat()</c>). A <see cref="TrackSize"/> converts to one.
/// </summary>
public readonly record struct GridTrack
{
    private GridTrack(TrackSize single, ImmutableArray<TrackSize> repeated, int count, Repeat? auto)
    {
        Single = single;
        Repeated = repeated;
        Count = count;
        AutoRepeat = auto;
    }

    /// <summary>
    /// Gets the track of an entry that is not a repetition.
    /// </summary>
    internal TrackSize Single { get; }

    /// <summary>
    /// Gets the tracks a repetition repeats; default for an entry that is not one.
    /// </summary>
    internal ImmutableArray<TrackSize> Repeated { get; }

    /// <summary>
    /// Gets the number of times a repetition repeats its tracks; 0 for an automatic repetition and for an entry that is not a
    /// repetition.
    /// </summary>
    internal int Count { get; }

    /// <summary>
    /// Gets how an automatic repetition repeats; <see langword="null"/> for any other entry.
    /// </summary>
    internal Repeat? AutoRepeat { get; }

    /// <summary>
    /// Gets a value indicating whether the entry repeats a track list.
    /// </summary>
    internal bool IsRepeat => !Repeated.IsDefault;

    /// <summary>
    /// Gets a value indicating whether the entry is an automatic repetition.
    /// </summary>
    internal bool IsAutoRepeat => AutoRepeat is not null;

    /// <summary>
    /// Returns the entry of one track.
    /// </summary>
    /// <param name="track">The track.</param>
    public static implicit operator GridTrack(TrackSize track) => new(track, default, 0, null);

    /// <summary>
    /// Returns the entry of one track.
    /// </summary>
    /// <param name="track">The track.</param>
    /// <returns>The entry.</returns>
    public static GridTrack FromTrackSize(TrackSize track) => track;

    /// <summary>
    /// Returns an entry that repeats <paramref name="tracks"/> <paramref name="count"/> times.
    /// </summary>
    /// <param name="count">The number of repetitions, at least 1.</param>
    /// <param name="tracks">The tracks, at least one.</param>
    /// <returns>The entry.</returns>
    internal static GridTrack Repeat(int count, ImmutableArray<TrackSize> tracks) => new(default, tracks, count, null);

    /// <summary>
    /// Returns an entry that repeats <paramref name="tracks"/> as many times as fit.
    /// </summary>
    /// <param name="auto">How it repeats.</param>
    /// <param name="tracks">The tracks, at least one.</param>
    /// <returns>The entry.</returns>
    internal static GridTrack Repeat(Repeat auto, ImmutableArray<TrackSize> tracks) => new(default, tracks, 0, auto);
}

/// <summary>
/// Makes the sizes of a grid's tracks (design §3.3, CSS Grid §7.2). Write <c>Track.Px(120)</c> for a fixed column,
/// <c>Track.Fr(1)</c> for a share of the free space, and <c>Track.MinMax(Track.Px(80), Track.Auto)</c> for a range.
/// </summary>
public static class Track
{
    /// <summary>
    /// The largest number of tracks a grid has on each side of its explicit grid's start, and the largest span (CSS Grid §8.4,
    /// "overlarge grids"; Taffy's <c>MAX_GRID_TRACKS</c>).
    /// </summary>
    internal const int MaxTracks = 10_000;

    /// <summary>
    /// Gets a track sized by its content within the free space: <c>auto</c>, whose minimum is the items' minimum contributions
    /// and whose maximum their max-content contributions.
    /// </summary>
    public static TrackSize Auto { get; } = new(TrackFunction.Auto, TrackFunction.Auto);

    /// <summary>
    /// Gets a track as large as the largest min-content contribution of its items (<c>min-content</c>).
    /// </summary>
    public static TrackSize MinContent { get; } = TrackSize.Of(new TrackFunction(TrackKind.MinContent, 0));

    /// <summary>
    /// Gets a track as large as the largest max-content contribution of its items (<c>max-content</c>).
    /// </summary>
    public static TrackSize MaxContent { get; } = TrackSize.Of(new TrackFunction(TrackKind.MaxContent, 0));

    /// <summary>
    /// Returns a track of a fixed size in logical pixels.
    /// </summary>
    /// <param name="pixels">The size, a finite number at or above 0.</param>
    /// <returns>The track size.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pixels"/> is negative or not finite.</exception>
    public static TrackSize Px(float pixels) => TrackSize.Of(new TrackFunction(TrackKind.Px, Check(pixels, nameof(pixels))));

    /// <summary>
    /// Returns a track that takes a percentage of the grid container's content box in its axis; a percentage of a size that is
    /// not known yet sizes the track as <see cref="Auto"/> (CSS Grid §7.2.1).
    /// </summary>
    /// <param name="percent">The percentage: 50 is half of the content box. A finite number at or above 0.</param>
    /// <returns>The track size.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="percent"/> is negative or not finite.</exception>
    public static TrackSize Percent(float percent) => TrackSize.Of(new TrackFunction(TrackKind.Percent, Check(percent, nameof(percent)) / 100));

    /// <summary>
    /// Returns a flexible track (<c>fr</c>): a share of the free space after the other tracks, in proportion to its factor
    /// (CSS Grid §7.2.4).
    /// </summary>
    /// <param name="factor">The flex factor, a finite number at or above 0.</param>
    /// <returns>The track size.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="factor"/> is negative or not finite.</exception>
    public static TrackSize Fr(float factor) => TrackSize.Of(new TrackFunction(TrackKind.Fr, Check(factor, nameof(factor))));

    /// <summary>
    /// Returns a track sized as <see cref="Auto"/> but no larger than <paramref name="limit"/>, unless its items' min-content
    /// contributions are larger (<c>fit-content()</c>, CSS Grid §7.2.4).
    /// </summary>
    /// <param name="limit">The limit in logical pixels, a finite number at or above 0.</param>
    /// <returns>The track size.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is negative or not finite.</exception>
    public static TrackSize FitContent(float limit) => TrackSize.Of(new TrackFunction(TrackKind.FitContentPx, Check(limit, nameof(limit))));

    /// <summary>
    /// Returns a track sized between <paramref name="min"/> and <paramref name="max"/> (<c>minmax()</c>, CSS Grid §7.2.4). When
    /// the maximum is smaller than the minimum, the minimum wins.
    /// </summary>
    /// <param name="min">The minimum: a length, a percentage, <see cref="Auto"/>, <see cref="MinContent"/>, or
    /// <see cref="MaxContent"/>.</param>
    /// <param name="max">The maximum: any track size but a <see cref="FitContent"/> one.</param>
    /// <returns>The track size.</returns>
    /// <exception cref="ArgumentException"><paramref name="min"/> is flexible or a <c>fit-content()</c> track, or
    /// <paramref name="max"/> is a <c>fit-content()</c> track.</exception>
    public static TrackSize MinMax(TrackSize min, TrackSize max)
    {
        if (min.Max.Kind is TrackKind.Fr or TrackKind.FitContentPx or TrackKind.FitContentPercent)
        {
            throw new ArgumentException("The minimum of a minmax() track is not flexible and not a fit-content() track.", nameof(min));
        }

        if (max.Max.Kind is TrackKind.FitContentPx or TrackKind.FitContentPercent)
        {
            throw new ArgumentException("The maximum of a minmax() track is not a fit-content() track.", nameof(max));
        }

        return new TrackSize(min.Min, max.Max);
    }

    /// <summary>
    /// Returns an entry of a track list that repeats <paramref name="tracks"/> <paramref name="count"/> times
    /// (<c>repeat()</c>, CSS Grid §7.2.3).
    /// </summary>
    /// <param name="count">The number of repetitions, from 1 to 10,000.</param>
    /// <param name="tracks">The tracks to repeat, at least one.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is below 1 or above 10,000.</exception>
    /// <exception cref="ArgumentException"><paramref name="tracks"/> is empty.</exception>
    public static GridTrack Repeat(int count, params ReadOnlySpan<TrackSize> tracks)
    {
        if (count is < 1 or > MaxTracks)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "A repetition repeats its tracks from 1 to 10,000 times.");
        }

        if (tracks.IsEmpty)
        {
            throw new ArgumentException("A repetition repeats at least one track.", nameof(tracks));
        }

        return GridTrack.Repeat(count, [.. tracks]);
    }

    /// <summary>
    /// Returns an entry of a track list that repeats <paramref name="tracks"/> as many times as fit in the grid container
    /// (<c>repeat(auto-fill)</c> and <c>repeat(auto-fit)</c>, CSS Grid §7.2.3.2): its size, else its maximum size, else its
    /// minimum size. A track list takes one such entry, and each of its tracks then needs a length or a percentage as its
    /// minimum or its maximum; a track list that breaks either is ignored, as CSS ignores it.
    /// </summary>
    /// <param name="repeat">How it repeats.</param>
    /// <param name="tracks">The tracks to repeat, at least one.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="repeat"/> is not a value of <see cref="XylemUI.Repeat"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="tracks"/> is empty.</exception>
    public static GridTrack Repeat(Repeat repeat, params ReadOnlySpan<TrackSize> tracks)
    {
        if (repeat is not (XylemUI.Repeat.AutoFill or XylemUI.Repeat.AutoFit))
        {
            throw new ArgumentOutOfRangeException(nameof(repeat), repeat, "An automatic repetition is AutoFill or AutoFit.");
        }

        if (tracks.IsEmpty)
        {
            throw new ArgumentException("A repetition repeats at least one track.", nameof(tracks));
        }

        return GridTrack.Repeat(repeat, [.. tracks]);
    }

    private static float Check(float value, string name) =>
        float.IsFinite(value) && value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(name, value, "A track's size is a finite number at or above 0.");
}

/// <summary>
/// A line that bounds a grid item, or a span (design §3.3, CSS Grid §8.3): the item's start or end in one axis.
/// </summary>
/// <remarks>
/// It is two bytes, as a node's style holds four (P02f): 0 is auto, a line its number, and a span 10,001 plus its tracks. A line
/// before -20,001 or after 10,001 lands on the same line as those, as the grid clamps every line into 10,000 tracks on each side
/// of the explicit grid's start (CSS Grid §8.4; Taffy's <c>into_origin_zero_line</c>), so it is kept as them.
/// </remarks>
public readonly record struct GridLine
{
    private const int SpanBase = Track.MaxTracks + 1;
    private const int LowestLine = -((2 * Track.MaxTracks) + 1);
    private readonly short _encoded;

    private GridLine(short encoded) => _encoded = encoded;

    /// <summary>
    /// Gets an end that the placement algorithm decides (<c>auto</c>, the default).
    /// </summary>
    public static GridLine Auto => default;

    /// <summary>
    /// Gets what the line is.
    /// </summary>
    internal GridLineKind Kind =>
        _encoded == 0 ? GridLineKind.Auto
        : _encoded > SpanBase ? GridLineKind.Span
        : GridLineKind.Line;

    /// <summary>
    /// Gets the line's number for <see cref="GridLineKind.Line"/>, or the number of tracks for <see cref="GridLineKind.Span"/>.
    /// </summary>
    internal int Value => _encoded > SpanBase ? _encoded - SpanBase : _encoded;

    /// <summary>
    /// Returns the line of a number: 1 is the start of the explicit grid, -1 its end, and they count on from there. Line 0 is no
    /// line and behaves as <see cref="Auto"/> (CSS Grid §8.3.1).
    /// </summary>
    /// <param name="line">The line's number, from -10,001 to 10,001.</param>
    /// <returns>The line.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="line"/> is out of the range.</exception>
    public static GridLine At(int line) =>
        line is >= -(Track.MaxTracks + 1) and <= Track.MaxTracks + 1
            ? new GridLine((short)line)
            : throw new ArgumentOutOfRangeException(nameof(line), line, "A grid line's number is from -10,001 to 10,001.");

    /// <summary>
    /// Returns an end that spans <paramref name="tracks"/> tracks from the other end (CSS Grid §8.3.1).
    /// </summary>
    /// <param name="tracks">The number of tracks, from 1 to 10,000.</param>
    /// <returns>The span.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tracks"/> is below 1 or above 10,000.</exception>
    public static GridLine Span(int tracks) =>
        tracks is >= 1 and <= Track.MaxTracks
            ? new GridLine((short)(SpanBase + tracks))
            : throw new ArgumentOutOfRangeException(nameof(tracks), tracks, "A span is from 1 to 10,000 tracks.");

    /// <summary>
    /// Returns a line or a span past the API's range, as a test of the grid's limits writes it: a line is kept from -20,001 to
    /// 10,001 and a span from 1 to 10,000, where the grid lands it anyway.
    /// </summary>
    /// <param name="kind">What the line is.</param>
    /// <param name="value">The line's number or the span.</param>
    /// <returns>The line.</returns>
    internal static GridLine Unchecked(GridLineKind kind, int value) =>
        kind switch
        {
            GridLineKind.Span => new GridLine((short)(SpanBase + Math.Clamp(value, 1, Track.MaxTracks))),
            GridLineKind.Line when value != 0 => new GridLine((short)Math.Clamp(value, LowestLine, Track.MaxTracks + 1)),
            _ => Auto,
        };
}

/// <summary>
/// The order in which the grid's auto-placement algorithm fills the cells (design §3.3, CSS Grid §7.7).
/// </summary>
public enum GridAutoFlow : byte
{
    /// <summary>
    /// Fills each row in turn, adding rows as needed (the default).
    /// </summary>
    Row,

    /// <summary>
    /// Fills each column in turn, adding columns as needed.
    /// </summary>
    Column,

    /// <summary>
    /// Fills each row in turn, and starts each item's search from the grid's start, so that a later item can fill a hole an
    /// earlier one left (<c>row dense</c>).
    /// </summary>
    RowDense,

    /// <summary>
    /// Fills each column in turn, and starts each item's search from the grid's start, so that a later item can fill a hole an
    /// earlier one left (<c>column dense</c>).
    /// </summary>
    ColumnDense,
}

/// <summary>
/// What a <see cref="GridLine"/> is.
/// </summary>
internal enum GridLineKind : byte
{
    /// <summary>
    /// An end that the placement algorithm decides.
    /// </summary>
    Auto,

    /// <summary>
    /// A line by its number.
    /// </summary>
    Line,

    /// <summary>
    /// A number of tracks from the other end.
    /// </summary>
    Span,
}

/// <summary>
/// The kind of a track sizing function (CSS Grid §7.2.1).
/// </summary>
internal enum TrackKind : byte
{
    /// <summary>
    /// A fixed length in logical pixels.
    /// </summary>
    Px,

    /// <summary>
    /// A fraction of the grid container's content box.
    /// </summary>
    Percent,

    /// <summary>
    /// The items' contributions: <c>auto</c>.
    /// </summary>
    Auto,

    /// <summary>
    /// The items' min-content contributions.
    /// </summary>
    MinContent,

    /// <summary>
    /// The items' max-content contributions.
    /// </summary>
    MaxContent,

    /// <summary>
    /// <c>fit-content()</c> of a length; only a maximum.
    /// </summary>
    FitContentPx,

    /// <summary>
    /// <c>fit-content()</c> of a fraction of the content box; only a maximum.
    /// </summary>
    FitContentPercent,

    /// <summary>
    /// A flex factor (<c>fr</c>); only a maximum.
    /// </summary>
    Fr,
}

/// <summary>
/// One track sizing function (CSS Grid §7.2.1; Taffy's <c>MinTrackSizingFunction</c> and <c>MaxTrackSizingFunction</c>).
/// </summary>
/// <param name="Kind">The kind.</param>
/// <param name="Value">The pixels, the fraction, or the flex factor; 0 for a kind without a value.</param>
internal readonly record struct TrackFunction(TrackKind Kind, float Value)
{
    /// <summary>
    /// Gets <c>auto</c>.
    /// </summary>
    public static TrackFunction Auto => new(TrackKind.Auto, 0);

    /// <summary>
    /// Gets a fixed zero, the size of a collapsed track.
    /// </summary>
    public static TrackFunction Zero => new(TrackKind.Px, 0);

    /// <summary>
    /// Gets a value indicating whether the function is intrinsic: <c>auto</c>, <c>min-content</c>, <c>max-content</c>, or
    /// <c>fit-content()</c>.
    /// </summary>
    public bool IsIntrinsic =>
        Kind is TrackKind.Auto or TrackKind.MinContent or TrackKind.MaxContent or TrackKind.FitContentPx or TrackKind.FitContentPercent;

    /// <summary>
    /// Gets a value indicating whether the maximum grows to the items' max-content contributions: <c>auto</c>,
    /// <c>max-content</c>, or <c>fit-content()</c>.
    /// </summary>
    public bool IsMaxContentAlike => Kind is TrackKind.Auto or TrackKind.MaxContent or TrackKind.FitContentPx or TrackKind.FitContentPercent;

    /// <summary>
    /// Gets a value indicating whether the function is <c>max-content</c> or <c>fit-content()</c>.
    /// </summary>
    public bool IsMaxOrFitContent => Kind is TrackKind.MaxContent or TrackKind.FitContentPx or TrackKind.FitContentPercent;

    /// <summary>
    /// Gets a value indicating whether the function is <c>fit-content()</c>.
    /// </summary>
    public bool IsFitContent => Kind is TrackKind.FitContentPx or TrackKind.FitContentPercent;

    /// <summary>
    /// Gets a value indicating whether the function resolves against the content box: a percentage or a
    /// <c>fit-content()</c> of one.
    /// </summary>
    public bool UsesPercentage => Kind is TrackKind.Percent or TrackKind.FitContentPercent;

    /// <summary>
    /// Returns the pixels of a fixed function against the content box's size, or NaN for an intrinsic or flexible one and for a
    /// percentage of an unknown size (Taffy's <c>definite_value</c>).
    /// </summary>
    /// <param name="basis">The content box's size in the axis, or NaN.</param>
    /// <returns>The pixels, or NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float DefiniteValue(float basis) =>
        Kind switch
        {
            TrackKind.Px => Value,
            TrackKind.Percent => Value * basis,
            _ => float.NaN,
        };

    /// <summary>
    /// Returns the limit of a <c>fit-content()</c> function against the content box's size, or NaN for another function and
    /// for a percentage of an unknown size (Taffy's <c>fit_content_limit</c>).
    /// </summary>
    /// <param name="basis">The content box's size in the axis, or NaN.</param>
    /// <returns>The pixels, or NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float FitContentLimit(float basis) =>
        Kind switch
        {
            TrackKind.FitContentPx => Value,
            TrackKind.FitContentPercent => Value * basis,
            _ => float.NaN,
        };

    /// <summary>
    /// Returns the limit a maximum puts on a track: the <c>fit-content()</c> argument, else the fixed value (Taffy's
    /// <c>definite_limit</c>).
    /// </summary>
    /// <param name="basis">The content box's size in the axis, or NaN.</param>
    /// <returns>The pixels, or NaN.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float DefiniteLimit(float basis) => IsFitContent ? FitContentLimit(basis) : DefiniteValue(basis);

    /// <summary>
    /// Returns whether a minimum behaves as <c>auto</c>: it is <c>auto</c>, or a percentage of a size not known yet (Taffy's
    /// <c>behaves_as_auto</c>).
    /// </summary>
    /// <param name="basis">The content box's size in the axis, or NaN.</param>
    /// <returns>Whether it behaves as <c>auto</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool BehavesAsAuto(float basis) => Kind == TrackKind.Auto || (Kind == TrackKind.Percent && float.IsNaN(basis));
}
