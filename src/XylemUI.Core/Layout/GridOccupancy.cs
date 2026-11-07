namespace XylemUI;

/// <summary>
/// The numbers of a grid's tracks in one axis (Taffy 0.15.1's <c>TrackCounts</c>): the implicit tracks before the explicit grid,
/// the explicit ones, and the implicit ones after.
/// </summary>
/// <remarks>
/// Lines are numbered from 0 at the start of the explicit grid, negative before it (Taffy's origin-zero coordinates). The track
/// list of an axis holds a line, then a track and its line after, so a track's index there is odd and a line's even.
/// </remarks>
/// <param name="NegativeImplicit">The implicit tracks before the explicit grid.</param>
/// <param name="Explicit">The explicit tracks.</param>
/// <param name="PositiveImplicit">The implicit tracks after the explicit grid.</param>
internal readonly record struct TrackCounts(int NegativeImplicit, int Explicit, int PositiveImplicit)
{
    /// <summary>
    /// Gets the number of tracks.
    /// </summary>
    public int Length => NegativeImplicit + Explicit + PositiveImplicit;

    /// <summary>
    /// Gets the first line of the implicit grid.
    /// </summary>
    public int ImplicitStartLine => -NegativeImplicit;

    /// <summary>
    /// Gets the last line of the implicit grid.
    /// </summary>
    public int ImplicitEndLine => Explicit + PositiveImplicit;

    /// <summary>
    /// Returns the index of the track after a line, counted from the first track of the implicit grid.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns>The track's index.</returns>
    public int LineToNextTrack(int line) => line + NegativeImplicit;

    /// <summary>
    /// Returns the line before a track given by its index from the first track of the implicit grid.
    /// </summary>
    /// <param name="track">The track's index.</param>
    /// <returns>The line.</returns>
    public int TrackToPreviousLine(int track) => track - NegativeImplicit;

    /// <summary>
    /// Returns the index of a line in the axis's track list, which holds the lines and the tracks in turn.
    /// </summary>
    /// <param name="line">The line, in the implicit grid.</param>
    /// <returns>The index.</returns>
    public int LineToTrackListIndex(int line) => 2 * (line + NegativeImplicit);
}

/// <summary>
/// The cells of a grid that its items occupy, while the placement algorithm runs (CSS Grid §8.5; Taffy 0.15.1's
/// <c>CellOccupancyMatrix</c>, <c>types/cell_occupancy.rs</c>). It keeps, for each row, the runs of occupied cells along it, and
/// for each column the same, so its size follows the items, not the cells: a grid may have 10,000 tracks in each axis.
/// </summary>
/// <remarks>
/// One instance serves every grid laid out on the thread: placement measures nothing, so no grid's placement runs inside
/// another's. The buffers grow and are kept, so a steady layout allocates nothing.
/// </remarks>
internal sealed class GridOccupancy
{
    // A run of cells of one state, from its start line to its end line along the track.
    private struct Interval
    {
        public int Start;
        public int End;
        public CellState State;
    }

    // The runs of one track, sorted and disjoint, with the end of the last auto-placed item that starts in it (the sparse
    // placement cursor).
    private struct TrackIntervals
    {
        public Interval[] Items;
        public int Count;
        public int Cursor;
        public bool HasCursor;

        public readonly bool IsEmpty => Count == 0;

        // The end of the last run that a search over the range collides with, less one: the last cell of that run.
        public readonly bool CollisionExtent(int start, int end, out int extent)
        {
            for (int i = Count - 1; i >= 0 && Items[i].End > start; i--)
            {
                if (Items[i].Start < end)
                {
                    extent = Items[i].End - 1;
                    return true;
                }
            }

            extent = 0;
            return false;
        }
    }

    private TrackIntervals[] _rows = [];
    private TrackIntervals[] _columns = [];
    private int _rowFront;
    private int _columnFront;
    private Interval[] _paint = new Interval[8];

    /// <summary>
    /// The state of a cell.
    /// </summary>
    public enum CellState : byte
    {
        /// <summary>
        /// No item.
        /// </summary>
        Unoccupied,

        /// <summary>
        /// An item placed at definite lines in both axes.
        /// </summary>
        DefinitelyPlaced,

        /// <summary>
        /// An item the auto-placement algorithm placed.
        /// </summary>
        AutoPlaced,
    }

    /// <summary>
    /// Gets the numbers of columns.
    /// </summary>
    public TrackCounts Columns { get; private set; }

    /// <summary>
    /// Gets the numbers of rows.
    /// </summary>
    public TrackCounts Rows { get; private set; }

    /// <summary>
    /// Empties the matrix and sizes it for estimated numbers of tracks, which it grows past as items need.
    /// </summary>
    /// <param name="columns">The estimated columns.</param>
    /// <param name="rows">The estimated rows.</param>
    public void Reset(TrackCounts columns, TrackCounts rows)
    {
        Columns = columns;
        Rows = rows;
        _columnFront = Prepare(ref _columns, columns.Length);
        _rowFront = Prepare(ref _rows, rows.Length);
    }

    /// <summary>
    /// Marks an area occupied, growing the grid to hold it.
    /// </summary>
    /// <param name="primaryIsColumns">Whether the primary axis of the auto-placement is the columns.</param>
    /// <param name="primaryStart">The area's start line in the primary axis.</param>
    /// <param name="primaryEnd">The area's end line in the primary axis.</param>
    /// <param name="secondaryStart">The area's start line in the secondary axis.</param>
    /// <param name="secondaryEnd">The area's end line in the secondary axis.</param>
    /// <param name="state">The state of the cells.</param>
    public void Mark(bool primaryIsColumns, int primaryStart, int primaryEnd, int secondaryStart, int secondaryEnd, CellState state)
    {
        (int rowStart, int rowEnd, int columnStart, int columnEnd) = primaryIsColumns
            ? (secondaryStart, secondaryEnd, primaryStart, primaryEnd)
            : (primaryStart, primaryEnd, secondaryStart, secondaryEnd);
        ExpandToFit(rowStart, rowEnd, columnStart, columnEnd);
        if (state == CellState.AutoPlaced && primaryStart < primaryEnd && secondaryStart < secondaryEnd)
        {
            ref TrackIntervals secondaryTrack = ref primaryIsColumns
                ? ref _rows[_rowFront + Rows.LineToNextTrack(secondaryStart)]
                : ref _columns[_columnFront + Columns.LineToNextTrack(secondaryStart)];
            if (!secondaryTrack.HasCursor || secondaryTrack.Cursor < primaryEnd)
            {
                secondaryTrack.Cursor = primaryEnd;
                secondaryTrack.HasCursor = true;
            }
        }

        for (int row = Rows.LineToNextTrack(rowStart); row < Rows.LineToNextTrack(rowEnd); row++)
        {
            Paint(ref _rows[_rowFront + row], columnStart, columnEnd, state);
        }

        for (int column = Columns.LineToNextTrack(columnStart); column < Columns.LineToNextTrack(columnEnd); column++)
        {
            Paint(ref _columns[_columnFront + column], rowStart, rowEnd, state);
        }
    }

    /// <summary>
    /// Finds whether an area holds occupied cells (Taffy's <c>line_area_collision_jump</c>): <see langword="false"/> when it is
    /// free, else <see langword="true"/> and the next search position along the primary axis that the occupied cells found do not
    /// block.
    /// </summary>
    /// <param name="primaryIsColumns">Whether the primary axis is the columns.</param>
    /// <param name="primaryStart">The area's start line in the primary axis.</param>
    /// <param name="primaryEnd">The area's end line in the primary axis.</param>
    /// <param name="secondaryStart">The area's start line in the secondary axis.</param>
    /// <param name="secondaryEnd">The area's end line in the secondary axis.</param>
    /// <param name="next">The next search position.</param>
    /// <returns>Whether the area holds occupied cells.</returns>
    public bool Collides(bool primaryIsColumns, int primaryStart, int primaryEnd, int secondaryStart, int secondaryEnd, out int next)
    {
        // The tracks along the primary axis, one for each line of the secondary one: rows when the primary axis is the columns.
        TrackIntervals[] tracks = primaryIsColumns ? _rows : _columns;
        int front = primaryIsColumns ? _rowFront : _columnFront;
        TrackCounts counts = primaryIsColumns ? Rows : Columns;
        int first = Math.Max(counts.LineToNextTrack(secondaryStart), 0);
        int last = Math.Min(counts.LineToNextTrack(secondaryEnd), counts.Length);
        bool found = false;
        int extent = 0;
        for (int i = first; i < last; i++)
        {
            if (tracks[front + i].CollisionExtent(primaryStart, primaryEnd, out int cell))
            {
                extent = found ? Math.Max(extent, cell) : cell;
                found = true;
            }
        }

        next = extent + 1;
        return found;
    }

    /// <summary>
    /// Finds whether any track in a span is occupied (Taffy's <c>occupied_track_jump</c>): when one is, the line after the last
    /// occupied one, from which an item that spans every track of the other axis may fit.
    /// </summary>
    /// <param name="columns">Whether the tracks are columns.</param>
    /// <param name="start">The span's start line.</param>
    /// <param name="end">The span's end line.</param>
    /// <param name="next">The line after the last occupied track.</param>
    /// <returns>Whether a track in the span is occupied.</returns>
    public bool OccupiedTrack(bool columns, int start, int end, out int next)
    {
        TrackIntervals[] tracks = columns ? _columns : _rows;
        int front = columns ? _columnFront : _rowFront;
        TrackCounts counts = columns ? Columns : Rows;
        int first = Math.Max(counts.LineToNextTrack(start), 0);
        int last = Math.Min(counts.LineToNextTrack(end), counts.Length);
        for (int i = last - 1; i >= first; i--)
        {
            if (!tracks[front + i].IsEmpty)
            {
                next = counts.TrackToPreviousLine(i) + 1;
                return true;
            }
        }

        next = 0;
        return false;
    }

    /// <summary>
    /// Returns whether a column holds an item.
    /// </summary>
    /// <param name="index">The column's index from the first column of the implicit grid.</param>
    /// <returns>Whether it holds an item.</returns>
    public bool ColumnIsOccupied(int index) => index < Columns.Length && !_columns[_columnFront + index].IsEmpty;

    /// <summary>
    /// Returns whether a row holds an item.
    /// </summary>
    /// <param name="index">The row's index from the first row of the implicit grid.</param>
    /// <returns>Whether it holds an item.</returns>
    public bool RowIsOccupied(int index) => index < Rows.Length && !_rows[_rowFront + index].IsEmpty;

    /// <summary>
    /// Returns the sparse placement cursor of a track of the secondary axis: the end, along the primary axis, of the last item
    /// auto-placed starting in it.
    /// </summary>
    /// <param name="primaryIsColumns">Whether the primary axis is the columns.</param>
    /// <param name="secondaryStart">The track's start line in the secondary axis.</param>
    /// <param name="cursor">The cursor.</param>
    /// <returns>Whether the track has one.</returns>
    public bool AutoPlacementCursor(bool primaryIsColumns, int secondaryStart, out int cursor)
    {
        TrackCounts counts = primaryIsColumns ? Rows : Columns;
        int index = counts.LineToNextTrack(secondaryStart);
        if (index < 0 || index >= counts.Length)
        {
            cursor = 0;
            return false;
        }

        ref TrackIntervals track = ref primaryIsColumns ? ref _rows[_rowFront + index] : ref _columns[_columnFront + index];
        cursor = track.Cursor;
        return track.HasCursor;
    }

    // Takes a buffer for the tracks with room before them, empties them, and returns where the first one is.
    private static int Prepare(ref TrackIntervals[] buffer, int count)
    {
        int needed = (2 * count) + 16;
        if (buffer.Length < needed)
        {
            Array.Resize(ref buffer, Math.Max(needed, buffer.Length * 2));
        }

        int front = (buffer.Length - count) / 2;
        for (int i = front; i < front + count; i++)
        {
            Clear(ref buffer[i]);
        }

        return front;
    }

    private static void Clear(ref TrackIntervals track)
    {
        track.Items ??= new Interval[2];
        track.Count = 0;
        track.HasCursor = false;
        track.Cursor = 0;
    }

    // Adds empty tracks before and after the grid to hold an area, as Taffy's expand_to_fit_range does; the runs are in lines from
    // the explicit grid, so the tracks already there keep theirs.
    private void ExpandToFit(int rowStart, int rowEnd, int columnStart, int columnEnd)
    {
        int negativeRows = Math.Max(-Rows.NegativeImplicit - rowStart, 0);
        int positiveRows = Math.Max(rowEnd - Rows.ImplicitEndLine, 0);
        int negativeColumns = Math.Max(-Columns.NegativeImplicit - columnStart, 0);
        int positiveColumns = Math.Max(columnEnd - Columns.ImplicitEndLine, 0);
        if (negativeRows + positiveRows > 0)
        {
            _rowFront = Grow(ref _rows, _rowFront, Rows.Length, negativeRows, positiveRows);
            Rows = Rows with { NegativeImplicit = Rows.NegativeImplicit + negativeRows, PositiveImplicit = Rows.PositiveImplicit + positiveRows };
        }

        if (negativeColumns + positiveColumns > 0)
        {
            _columnFront = Grow(ref _columns, _columnFront, Columns.Length, negativeColumns, positiveColumns);
            Columns = Columns with
            {
                NegativeImplicit = Columns.NegativeImplicit + negativeColumns,
                PositiveImplicit = Columns.PositiveImplicit + positiveColumns,
            };
        }
    }

    // Makes room for tracks before and after the ones in use, keeping theirs, and empties the new ones. Returns the new front.
    private static int Grow(ref TrackIntervals[] buffer, int front, int count, int before, int after)
    {
        if (front < before || front + count + after > buffer.Length)
        {
            // Only a grid larger than any before gets here; the tracks not in use get their buffers when they are cleared.
            var larger = new TrackIntervals[Math.Max(buffer.Length * 2, (2 * (count + before + after)) + 16)];
            int newFront = ((larger.Length - (count + before + after)) / 2) + before;
            Array.Copy(buffer, front, larger, newFront, count);
            buffer = larger;
            front = newFront;
        }

        for (int i = front - before; i < front; i++)
        {
            Clear(ref buffer[i]);
        }

        for (int i = front + count; i < front + count + after; i++)
        {
            Clear(ref buffer[i]);
        }

        return front - before;
    }

    // Sets the cells of a range of a track to a state, over the state of cells already occupied (Taffy's TrackIntervals::paint).
    private void Paint(ref TrackIntervals track, int start, int end, CellState state)
    {
        if (start >= end)
        {
            return;
        }

        // The common case: the range lies after every run, as the placement mostly moves forward.
        if (track.Count == 0 || track.Items[track.Count - 1].End <= start)
        {
            if (track.Count > 0 && track.Items[track.Count - 1].State == state && track.Items[track.Count - 1].End == start)
            {
                track.Items[track.Count - 1].End = end;
            }
            else
            {
                Append(
                    ref track.Items,
                    ref track.Count,
                    new Interval
                    {
                        Start = start,
                        End = end,
                        State = state,
                    }
                );
            }

            return;
        }

        int count = 0;
        for (int i = 0; i < track.Count; i++)
        {
            Interval run = track.Items[i];
            if (run.End <= start)
            {
                Append(ref _paint, ref count, run);
            }
            else if (run.Start < start)
            {
                Append(ref _paint, ref count, run with { End = start });
            }
        }

        if (count > 0 && _paint[count - 1].State == state && _paint[count - 1].End == start)
        {
            _paint[count - 1].End = end;
        }
        else
        {
            Append(
                ref _paint,
                ref count,
                new Interval
                {
                    Start = start,
                    End = end,
                    State = state,
                }
            );
        }

        for (int i = 0; i < track.Count; i++)
        {
            Interval run = track.Items[i];
            Interval trimmed;
            if (run.Start >= end)
            {
                trimmed = run;
            }
            else if (run.End > end)
            {
                trimmed = run with { Start = end };
            }
            else
            {
                continue;
            }

            if (_paint[count - 1].State == trimmed.State && _paint[count - 1].End == trimmed.Start)
            {
                _paint[count - 1].End = trimmed.End;
            }
            else
            {
                Append(ref _paint, ref count, trimmed);
            }
        }

        if (track.Items.Length < count)
        {
            Array.Resize(ref track.Items, Math.Max(count, track.Items.Length * 2));
        }

        Array.Copy(_paint, track.Items, count);
        track.Count = count;
    }

    private static void Append(ref Interval[] runs, ref int count, Interval run)
    {
        if (count == runs.Length)
        {
            Array.Resize(ref runs, runs.Length * 2);
        }

        runs[count++] = run;
    }
}
