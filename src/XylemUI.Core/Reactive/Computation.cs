using System.Runtime.CompilerServices;

namespace XylemUI;

/// <summary>
/// The base of computed values and effects: a node that runs code and records what the code read (design §2.2.2). It has no
/// public members.
/// </summary>
/// <remarks>
/// <para>
/// The sources are kept in the order they were read. A run that reads the same sources in the same order leaves every edge in
/// place; a run that reads something else at position <c>n</c> relinks only from <c>n</c> on.
/// </para>
/// <para>
/// A computation registers with its sources (an observer edge on their side) only while it is live: an effect always, a computed
/// value while something reads it. A source edge of a computation that is not live has <see cref="SourceEdge.SlotInSource"/>
/// -1 (design §2.2.2, the ownership of computed values).
/// </para>
/// <para>
/// A dirty computation is brought up to date by pulling: each source, in order, is brought up to date and its version compared
/// with the one this computation saw. The computation runs again only when one of them changed.
/// </para>
/// </remarks>
public abstract class Computation : ReactiveNode
{
    private SourceEdge[] _sources = [];
    private uint _depth;

    // The position of the next read in this run.
    private int _readCursor;

    /// <summary>
    /// Gets or sets the status of this computation.
    /// </summary>
    internal ComputationStatus Status { get; set; } = ComputationStatus.Dirty;

    /// <inheritdoc />
    internal override uint Depth => _depth;

    /// <summary>
    /// Gets the number of sources read in the last run.
    /// </summary>
    internal int SourceCount { get; private set; }

    /// <summary>
    /// Gets the sources read in the last run, in the order they were read.
    /// </summary>
    internal ReadOnlySpan<SourceEdge> Sources => _sources.AsSpan(0, SourceCount);

    /// <summary>
    /// Gets a value indicating whether this computation was disposed. A disposed computation has no sources and never runs.
    /// </summary>
    internal bool IsDisposed { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this computation has run at least once.
    /// </summary>
    private protected bool HasRun { get; private set; }

    /// <summary>
    /// Gets a value indicating whether this computation is registered with its sources, so that their changes mark it dirty.
    /// </summary>
    private protected abstract bool IsLive { get; }

    private protected Computation() { }

    /// <summary>
    /// Marks this computation dirty because a source changed (design §2.2.2, the state transitions).
    /// </summary>
    internal abstract void MarkDirty();

    /// <summary>
    /// Records a read of <paramref name="source"/> during this computation's run.
    /// </summary>
    internal void RegisterDependency(ReactiveNode source)
    {
        // A computation disposed while it runs records nothing more.
        if (IsDisposed)
        {
            return;
        }

        int position = _readCursor++;
        if (position < SourceCount && ReferenceEquals(_sources[position].Source, source))
        {
            _sources[position].SeenVersion = source.Version;
        }
        else
        {
            RemoveSourcesFrom(position);
            AddSource(source);
        }

        uint depth = source.Depth + 1;
        if (depth > _depth)
        {
            _depth = depth;
        }
    }

    /// <summary>
    /// Updates the position of an edge in <see cref="Sources"/> after its source moved the matching observer edge.
    /// </summary>
    internal void MoveSourceSlot(int position, int newSlotInSource) => _sources[position].SlotInSource = newSlotInSource;

    /// <summary>
    /// Returns a value indicating whether a source changed since this computation last read it, bringing computed sources up to
    /// date on the way. Sources are checked in the order they were read and the check stops at the first change, so a source
    /// that the next run may no longer read is not evaluated.
    /// </summary>
    private protected bool AnySourceChanged()
    {
        for (int i = 0; i < SourceCount; i++)
        {
            ReactiveNode source = _sources[i].Source;
            if (source is Computation computed)
            {
                computed.Refresh();
            }

            if (source.Version != _sources[i].SeenVersion)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Brings this computation up to date when it is read. Only computed values refresh this way; an effect is run by the
    /// scheduler.
    /// </summary>
    internal virtual void Refresh() { }

    /// <summary>
    /// Runs the body with this computation as the reader, then drops the sources it did not read this time.
    /// </summary>
    private protected void Run()
    {
        Status = ComputationStatus.Running;
        _readCursor = 0;
        _depth = 0;
        Computation? previous = ReactiveRuntime.Current;
        ReactiveRuntime.Current = this;
        try
        {
            Execute();
        }
        catch (Exception exception) when (exception is not InvariantViolationException)
        {
            OnException(exception);
        }
        finally
        {
            ReactiveRuntime.Current = previous;
            RemoveSourcesFrom(_readCursor);
            HasRun = true;
        }

        if (Status == ComputationStatus.Running)
        {
            Status = ComputationStatus.Clean;
        }
    }

    /// <summary>
    /// Runs the body. Reads inside it are recorded as sources.
    /// </summary>
    private protected abstract void Execute();

    /// <summary>
    /// Handles an exception thrown by the body (design §2.2.2, exceptions).
    /// </summary>
    private protected abstract void OnException(Exception exception);

    /// <summary>
    /// Removes every source edge and stops this computation from running again.
    /// </summary>
    private protected void DisposeSources()
    {
        IsDisposed = true;
        RemoveSourcesFrom(0);
        Status = ComputationStatus.Clean;
    }

    private void AddSource(ReactiveNode source)
    {
        if (SourceCount == _sources.Length)
        {
            GrowSources();
        }

        int position = SourceCount++;
        int slot = IsLive ? source.AddObserver(this, position) : -1;
        _sources[position] = new SourceEdge(source, slot, source.Version);
    }

    // The edges removed are the last ones in this computation's array, so only the source's side needs a swap.
    private void RemoveSourcesFrom(int position)
    {
        for (int i = SourceCount - 1; i >= position; i--)
        {
            SourceEdge edge = _sources[i];
            _sources[i] = default;
            if (edge.SlotInSource >= 0)
            {
                edge.Source.RemoveObserver(edge.SlotInSource);
            }
        }

        if (position < SourceCount)
        {
            SourceCount = position;
        }
    }

    /// <summary>
    /// Registers with every source this computation is not registered with (it became live).
    /// </summary>
    private protected void LinkSources()
    {
        for (int i = 0; i < SourceCount; i++)
        {
            if (_sources[i].SlotInSource < 0)
            {
                _sources[i].SlotInSource = _sources[i].Source.AddObserver(this, i);
            }
        }
    }

    /// <summary>
    /// Leaves every source this computation is registered with, keeping the edges and the versions seen (it is no longer live).
    /// </summary>
    private protected void UnlinkSources()
    {
        for (int i = 0; i < SourceCount; i++)
        {
            int slot = _sources[i].SlotInSource;
            if (slot >= 0)
            {
                _sources[i].SlotInSource = -1;
                _sources[i].Source.RemoveObserver(slot);
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowSources() => Array.Resize(ref _sources, Math.Max(4, _sources.Length * 2));
}
