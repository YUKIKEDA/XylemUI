namespace XylemUI;

/// <summary>
/// The shape of the mouse cursor over a node (design §7, "Pointer input"). Each has a Win32 cursor (<c>IDC_*</c>); the rows of Linux
/// and macOS write their own.
/// </summary>
internal enum CursorKind : byte
{
    /// <summary>
    /// The arrow (<c>IDC_ARROW</c>), where no node gives a shape.
    /// </summary>
    Arrow,

    /// <summary>
    /// The text cursor (<c>IDC_IBEAM</c>).
    /// </summary>
    IBeam,

    /// <summary>
    /// The hand of a link (<c>IDC_HAND</c>).
    /// </summary>
    Hand,

    /// <summary>
    /// The crosshair (<c>IDC_CROSS</c>).
    /// </summary>
    Crosshair,

    /// <summary>
    /// The hourglass (<c>IDC_WAIT</c>).
    /// </summary>
    Wait,

    /// <summary>
    /// The arrow with an hourglass (<c>IDC_APPSTARTING</c>).
    /// </summary>
    Progress,

    /// <summary>
    /// The arrow with a question mark (<c>IDC_HELP</c>).
    /// </summary>
    Help,

    /// <summary>
    /// The slashed circle (<c>IDC_NO</c>).
    /// </summary>
    NotAllowed,

    /// <summary>
    /// The four-pointed arrow (<c>IDC_SIZEALL</c>).
    /// </summary>
    ResizeAll,

    /// <summary>
    /// The arrow pointing left and right (<c>IDC_SIZEWE</c>).
    /// </summary>
    ResizeEW,

    /// <summary>
    /// The arrow pointing up and down (<c>IDC_SIZENS</c>).
    /// </summary>
    ResizeNS,

    /// <summary>
    /// The arrow pointing to the top left and the bottom right (<c>IDC_SIZENWSE</c>).
    /// </summary>
    ResizeNWSE,

    /// <summary>
    /// The arrow pointing to the top right and the bottom left (<c>IDC_SIZENESW</c>).
    /// </summary>
    ResizeNESW,
}
