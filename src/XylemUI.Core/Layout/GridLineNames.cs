namespace XylemUI;

/// <summary>
/// The names of grid lines and the named placements of grid items, for the whole process (design §3.3, "how lines are held"):
/// a name is kept as a number, and a placement by name as a number that fits in the two bytes of a <see cref="GridLine"/>.
/// </summary>
/// <remarks>
/// Entries are only added, under a lock, when an app makes a line or a placement; the layout only reads them. The same name and
/// the same placement give the same number, so the table grows with the distinct names and placements an app writes, which
/// are identifiers in its code. A placement past <see cref="Capacity"/> throws.
/// </remarks>
internal static class GridLineNames
{
    /// <summary>
    /// The largest number of distinct named placements: the values of a <see cref="GridLine"/> that lines and spans do not use.
    /// </summary>
    public const int Capacity = (short.MaxValue - FirstPositive + 1) + (LastNegative - short.MinValue + 1);

    /// <summary>
    /// The first value of a <see cref="GridLine"/> that holds a named placement, above the spans.
    /// </summary>
    public const int FirstPositive = (2 * Track.MaxTracks) + 2;

    /// <summary>
    /// The first value below the lines that holds a named placement, counting down.
    /// </summary>
    public const int LastNegative = -((2 * Track.MaxTracks) + 2);

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, int> NameIds = new(StringComparer.Ordinal);
    private static readonly Dictionary<Placement, int> PlacementIds = [];
    private static Placement[] _placements = new Placement[16];

    /// <summary>
    /// Returns the number of a line's name, adding the name when it is new.
    /// </summary>
    /// <param name="name">The name, checked by the caller.</param>
    /// <returns>The number.</returns>
    public static int NameId(string name)
    {
        lock (Gate)
        {
            return NameIdLocked(name);
        }
    }

    /// <summary>
    /// Returns the number of a named placement, adding it when it is new.
    /// </summary>
    /// <param name="name">The name, checked by the caller.</param>
    /// <param name="index">Which line of the name: 0 for the name alone, else from 1, or from -1 back from the end.</param>
    /// <param name="span">Whether it spans to the line rather than naming it.</param>
    /// <returns>The number, from 0 to <see cref="Capacity"/> less one.</returns>
    /// <exception cref="InvalidOperationException">The process already holds <see cref="Capacity"/> named placements.</exception>
    public static int PlacementId(string name, int index, bool span)
    {
        lock (Gate)
        {
            int nameId = NameIdLocked(name);

            // The name alone first looks for the edge of the area the lines `<name>-start` and `<name>-end` make.
            bool alone = index == 0 && !span;
            var placement = new Placement(
                nameId,
                (short)index,
                span,
                alone ? NameIdLocked(name + "-start") : -1,
                alone ? NameIdLocked(name + "-end") : -1
            );
            if (PlacementIds.TryGetValue(placement, out int id))
            {
                return id;
            }

            id = PlacementIds.Count;
            if (id == Capacity)
            {
                throw new InvalidOperationException(
                    $"The process holds {Capacity} distinct named grid placements, the most it can; grid line names are identifiers written in the app's code, not data."
                );
            }

            Placement[] placements = _placements;
            if (id == placements.Length)
            {
                Array.Resize(ref placements, placements.Length * 2);
            }

            placements[id] = placement;
            Volatile.Write(ref _placements, placements);
            PlacementIds.Add(placement, id);
            return id;
        }
    }

    /// <summary>
    /// Returns a named placement by its number.
    /// </summary>
    /// <param name="id">The number <see cref="PlacementId"/> returned.</param>
    /// <returns>The placement.</returns>
    public static Placement PlacementOf(int id) => Volatile.Read(ref _placements)[id];

    /// <summary>
    /// Checks a name of a grid line as CSS does: not empty, and not a word that grid placements read (<c>span</c>,
    /// <c>auto</c>).
    /// </summary>
    /// <param name="name">The line's name to check.</param>
    /// <param name="parameter">The name of the parameter that holds it.</param>
    /// <returns>The same name.</returns>
    /// <exception cref="ArgumentException">The name is empty, <c>span</c>, or <c>auto</c>.</exception>
    public static string Check(string name, string parameter)
    {
        ArgumentNullException.ThrowIfNull(name, parameter);
        return name.Length == 0 || name is "span" or "auto"
            ? throw new ArgumentException("A grid line's name is not empty, and not span or auto.", parameter)
            : name;
    }

    private static int NameIdLocked(string name)
    {
        if (!NameIds.TryGetValue(name, out int id))
        {
            id = NameIds.Count;
            NameIds.Add(name, id);
        }

        return id;
    }

    /// <summary>
    /// A grid item's placement by a line's name.
    /// </summary>
    /// <param name="Name">The number of the name.</param>
    /// <param name="Index">Which line of the name: 0 for the name alone, else from 1, or from -1 back from the end.</param>
    /// <param name="IsSpan">Whether it spans to the line rather than naming it.</param>
    /// <param name="StartEdge">For the name alone, the number of <c>&lt;name&gt;-start</c>; else -1.</param>
    /// <param name="EndEdge">For the name alone, the number of <c>&lt;name&gt;-end</c>; else -1.</param>
    internal readonly record struct Placement(int Name, short Index, bool IsSpan, int StartEdge, int EndEdge);
}
