namespace XylemUI;

/// <summary>
/// How a format of one of Flutter's 41 skeletons writes a range of two dates (design §4.11, "Interval formats"), read from a locale's
/// interval table, whose layout tools/CldrTableGen/DateTables.cs describes: for each field the two dates can first differ in, the
/// earlier date alone, both dates joined by the locale's <c>intervalFormatFallback</c>, or an interval pattern split into a part for
/// each date. tools/CldrTableGen chose the patterns with ICU's <c>DateIntervalFormat</c>, so nothing is chosen at run time.
/// </summary>
internal sealed class DateRangeFormat
{
    /// <summary>
    /// The number of fields a range compares: the year, the month, the day, am or pm, the hour, the minute, and the second.
    /// </summary>
    public const int FieldCount = 7;

    // An entry's count byte that says it is another entry (a 24-hour entry that is the skeleton's in the locale's hour), the kinds of
    // record, and the field bytes: one date, the fallback with the format's own pattern, and the first record.
    private const int SameEntry = 255;
    private const int SplitLaterFirst = 1;
    private const int FallbackRecord = 2;
    private const int SingleField = 0;
    private const int OwnFallbackField = 1;
    private const int FirstRecordField = 2;

    private readonly Slot[] _slots;

    private DateRangeFormat(string fallback, Slot[] slots)
    {
        // tools/CldrTableGen checks that the fallback has {0} and {1} once each.
        int earlier = fallback.IndexOf("{0}", StringComparison.Ordinal);
        int later = fallback.IndexOf("{1}", StringComparison.Ordinal);
        LaterFirst = later < earlier;
        int first = Math.Min(earlier, later);
        int second = Math.Max(earlier, later);
        Before = fallback[..first];
        Between = fallback[(first + 3)..second];
        After = fallback[(second + 3)..];
        _slots = slots;
    }

    /// <summary>
    /// The ways a range is written.
    /// </summary>
    public enum SlotKind : byte
    {
        /// <summary>
        /// The earlier date alone, with the format's pattern.
        /// </summary>
        Single,

        /// <summary>
        /// Both dates, each with a pattern, joined by the fallback.
        /// </summary>
        Fallback,

        /// <summary>
        /// The first part written with one date and the second part with the other.
        /// </summary>
        Split,
    }

    /// <summary>
    /// Gets the text of <c>intervalFormatFallback</c> before its first date.
    /// </summary>
    public string Before { get; }

    /// <summary>
    /// Gets the text of <c>intervalFormatFallback</c> between its dates.
    /// </summary>
    public string Between { get; }

    /// <summary>
    /// Gets the text of <c>intervalFormatFallback</c> after its second date.
    /// </summary>
    public string After { get; }

    /// <summary>
    /// Gets a value indicating whether the fallback writes the later date first (<c>{1}</c> before <c>{0}</c>).
    /// </summary>
    public bool LaterFirst { get; }

    /// <summary>
    /// Reads the patterns of one entry of an interval table.
    /// </summary>
    /// <param name="table">The locale's interval table.</param>
    /// <param name="entry">The entry: the skeleton's among the 37 without a time zone, or one of the 3 24-hour entries after them.</param>
    /// <param name="ownEntry">The entry the table points a 24-hour entry to when it is the skeleton's entry in the locale's hour;
    /// <paramref name="entry"/> for the others.</param>
    /// <returns>The patterns.</returns>
    public static DateRangeFormat Read(ReadOnlySpan<byte> table, int entry, int ownEntry)
    {
        var reader = new CldrTableReader(table);
        string fallback = reader.ReadString();
        CldrTableReader at = SkipTo(reader, entry);
        int count = at.ReadByte();
        if (count == SameEntry)
        {
            at = SkipTo(reader, ownEntry);
            count = at.ReadByte();
        }

        Span<int> fields = stackalloc int[FieldCount];
        for (int i = 0; i < FieldCount; i++)
        {
            fields[i] = at.ReadByte();
        }

        var records = new Slot[count];
        for (int i = 0; i < count; i++)
        {
            int kind = at.ReadByte();
            DatePatternItem[] first = DatePattern.Compile(at.ReadString());
            records[i] =
                kind == FallbackRecord
                    ? new Slot(SlotKind.Fallback, first, null, false)
                    : new Slot(SlotKind.Split, first, DatePattern.Compile(at.ReadString()), kind == SplitLaterFirst);
        }

        var slots = new Slot[FieldCount];
        for (int i = 0; i < FieldCount; i++)
        {
            slots[i] = fields[i] switch
            {
                SingleField => default,
                OwnFallbackField => new Slot(SlotKind.Fallback, null, null, false),
                int record => records[record - FirstRecordField],
            };
        }

        return new DateRangeFormat(fallback, slots);
    }

    /// <summary>
    /// Returns the first field in which two dates differ, in the order of ICU's <c>DateIntervalFormat::formatImpl</c>: the year,
    /// the month, the day, am or pm, the hour, the minute, and the second. The era is not compared: a <see cref="DateTime"/> is always
    /// of the common era.
    /// </summary>
    /// <param name="start">The earlier date.</param>
    /// <param name="end">The later date.</param>
    /// <returns>The field's index, from 0 for the year to 6 for the second; -1 when the dates differ in no field down to the
    /// second.</returns>
    public static int GreatestDifference(DateTime start, DateTime end)
    {
        if (start.Year != end.Year)
        {
            return 0;
        }

        if (start.Month != end.Month)
        {
            return 1;
        }

        if (start.Day != end.Day)
        {
            return 2;
        }

        if ((start.Hour >= 12) != (end.Hour >= 12))
        {
            return 3;
        }

        if (start.Hour != end.Hour)
        {
            return 4;
        }

        if (start.Minute != end.Minute)
        {
            return 5;
        }

        return start.Second != end.Second ? 6 : -1;
    }

    /// <summary>
    /// Returns how a range whose dates first differ in a field is written.
    /// </summary>
    /// <param name="field">The field, from 0 for the year to 6 for the second.</param>
    /// <returns>The way and its patterns.</returns>
    public Slot SlotOf(int field) => _slots[field];

    // The reader at the start of an entry: each entry before it is skipped.
    private static CldrTableReader SkipTo(CldrTableReader reader, int entry)
    {
        for (int i = 0; i < entry; i++)
        {
            int count = reader.ReadByte();
            if (count == SameEntry)
            {
                continue;
            }

            for (int field = 0; field < FieldCount; field++)
            {
                _ = reader.ReadByte();
            }

            for (int record = 0; record < count; record++)
            {
                int kind = reader.ReadByte();
                reader.SkipString();
                if (kind != FallbackRecord)
                {
                    reader.SkipString();
                }
            }
        }

        return reader;
    }

    /// <summary>
    /// How a range whose dates first differ in one field is written. The default value writes the earlier date alone.
    /// </summary>
    /// <param name="Kind">The way.</param>
    /// <param name="First">For <see cref="SlotKind.Split"/>, the first part; for <see cref="SlotKind.Fallback"/>, the pattern of
    /// each date, <see langword="null"/> for the format's own.</param>
    /// <param name="Second">For <see cref="SlotKind.Split"/>, the second part.</param>
    /// <param name="LaterFirst">For <see cref="SlotKind.Split"/>, whether the first part is written with the later date.</param>
    public readonly record struct Slot(SlotKind Kind, DatePatternItem[]? First, DatePatternItem[]? Second, bool LaterFirst);
}
