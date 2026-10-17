namespace XylemUI;

/// <summary>
/// Why a node is laid out from (design §2.4.1). The values keep the design's priority order (larger wins) for the reasons
/// later rows add.
/// </summary>
internal enum LayoutReason : byte
{
    /// <summary>The content inside a boundary changed. The boundary's last constraint is still valid.</summary>
    IntrinsicSizeChanged = 0,

    /// <summary>The window's size changed. The request carries the new size, which the operating system decides.</summary>
    WindowResize = 3,
}
