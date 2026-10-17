namespace XylemUI;

/// <summary>
/// Where a computation is in its update (design §2.2.2, the state transitions).
/// </summary>
internal enum ComputationStatus : byte
{
    /// <summary>The value is current for its sources.</summary>
    Clean,

    /// <summary>At least one source may have changed.</summary>
    Dirty,

    /// <summary>Effects only: waiting in the scheduler's queue.</summary>
    Queued,

    /// <summary>Running now.</summary>
    Running,
}
