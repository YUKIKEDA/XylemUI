using System.Globalization;
using System.Text.Json;

namespace XylemUI.Tools.CldrTableGen;

/// <summary>
/// The week of a region (CLDR's <c>weekData</c>): the first day, the minimal days of the first week, and the first and last day of
/// the weekend (design §4.11, "Names and the week" and "Added to the scope").
/// </summary>
/// <param name="FirstDay">The first day of the week, as a <c>DayOfWeek</c> member's name.</param>
/// <param name="MinimalDays">The days of the new year that the first week holds at least (1 to 7).</param>
/// <param name="WeekendStart">The first day of the weekend.</param>
/// <param name="WeekendEnd">The last day of the weekend.</param>
internal sealed record Week(string FirstDay, int MinimalDays, string WeekendStart, string WeekendEnd);

/// <summary>
/// Looks up the week of a region in <c>cldr-core/supplemental/weekData.json</c>.
/// </summary>
internal sealed class WeekData
{
    private readonly JsonElement _data;

    private WeekData(JsonElement data)
    {
        _data = data;
    }

    /// <summary>
    /// Reads the week data of a zip.
    /// </summary>
    /// <param name="cldr">The zip.</param>
    /// <returns>The week data.</returns>
    public static WeekData Read(CldrZip cldr) => new(cldr.Supplemental("weekData").GetProperty("weekData"));

    /// <summary>
    /// Returns the week of a region. A region the data does not list takes the values the data gives the world (<c>001</c>); the
    /// alternative values (<c>GB-alt-variant</c>) are not read.
    /// </summary>
    /// <param name="region">The region, such as <c>JP</c> or <c>419</c>.</param>
    /// <returns>The week.</returns>
    public Week For(string region)
    {
        int minimalDays = int.Parse(Value("minDays", region), CultureInfo.InvariantCulture);
        if (minimalDays is < 1 or > 7)
        {
            throw new InvalidDataException("The minimal days of " + region + " are " + Value("minDays", region) + ".");
        }

        return new Week(Day(Value("firstDay", region)), minimalDays, Day(Value("weekendStart", region)), Day(Value("weekendEnd", region)));
    }

    private static string Day(string key) =>
        key switch
        {
            "sun" => "Sunday",
            "mon" => "Monday",
            "tue" => "Tuesday",
            "wed" => "Wednesday",
            "thu" => "Thursday",
            "fri" => "Friday",
            "sat" => "Saturday",
            _ => throw new InvalidDataException("The day " + key + " is not one of weekData's."),
        };

    private string Value(string kind, string region)
    {
        JsonElement values = _data.GetProperty(kind);
        if (values.TryGetProperty(region, out JsonElement value) || values.TryGetProperty("001", out value))
        {
            return value.GetString() ?? throw new InvalidDataException("weekData's " + kind + " of " + region + " is not a string.");
        }

        throw new InvalidDataException("weekData has no " + kind + " of " + region + " and none of the world (001).");
    }
}
