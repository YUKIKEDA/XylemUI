namespace XylemUI;

/// <summary>
/// Why a node is laid out from (design §2.4.1). The values keep the design's priority order (larger wins) for the reasons
/// later rows add.
/// </summary>
internal enum LayoutReason : byte
{
    /// <summary>The content inside a boundary changed. The boundary's last constraint is still valid.</summary>
    IntrinsicSizeChanged = 0,

    /// <summary>
    /// A virtualized list made or took out rows (design §3.4). The list's box is laid out from its last constraint, which its parent
    /// gave and which still holds.
    /// </summary>
    MaterializationChanged = 2,

    /// <summary>The window's size changed. The request carries the new size, which the operating system decides.</summary>
    WindowResize = 3,
}
