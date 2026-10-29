using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows;

/// <summary>
/// Reads what Win32 keeps of a pointer message (design §7, "Touch and pen"): the pointer's type, its information, a pen's or a
/// touch's, and their coalesced history. The window reads through it so that the tests can hand it the information of a touch or
/// a pen with no device, as Flutter's <c>MockWindowsProcTable</c> does (<c>flutter_window_unittests.cc</c> lines 86 and 291 to
/// 333).
/// </summary>
internal interface IPointerReader
{
    /// <summary>
    /// Reads the pointer's type (<c>GetPointerType</c>).
    /// </summary>
    /// <param name="pointerId">Win32's pointer id.</param>
    /// <param name="type">The type.</param>
    /// <returns>Whether it was read.</returns>
    bool TryGetType(uint pointerId, out POINTER_INPUT_TYPE type);

    /// <summary>
    /// Reads a pen's information for the message being handled (<c>GetPointerPenInfo</c>).
    /// </summary>
    /// <param name="pointerId">Win32's pointer id.</param>
    /// <param name="info">The information.</param>
    /// <returns>Whether it was read.</returns>
    bool TryGetPenInfo(uint pointerId, out POINTER_PEN_INFO info);

    /// <summary>
    /// Reads a touch's information for the message being handled (<c>GetPointerTouchInfo</c>).
    /// </summary>
    /// <param name="pointerId">Win32's pointer id.</param>
    /// <param name="info">The information.</param>
    /// <returns>Whether it was read.</returns>
    bool TryGetTouchInfo(uint pointerId, out POINTER_TOUCH_INFO info);

    /// <summary>
    /// Reads a pen's coalesced history for the message being handled, newest first (<c>GetPointerPenInfoHistory</c>).
    /// </summary>
    /// <param name="pointerId">Win32's pointer id.</param>
    /// <param name="destination">Where to write.</param>
    /// <returns>The entries written; 0 when none could be read.</returns>
    int ReadPenHistory(uint pointerId, Span<POINTER_PEN_INFO> destination);

    /// <summary>
    /// Reads a touch's coalesced history for the message being handled, newest first (<c>GetPointerTouchInfoHistory</c>).
    /// </summary>
    /// <param name="pointerId">Win32's pointer id.</param>
    /// <param name="destination">Where to write.</param>
    /// <returns>The entries written; 0 when none could be read.</returns>
    int ReadTouchHistory(uint pointerId, Span<POINTER_TOUCH_INFO> destination);
}

/// <summary>
/// Reads the pointer messages' information from Win32.
/// </summary>
internal sealed unsafe class Win32PointerReader : IPointerReader
{
    private Win32PointerReader() { }

    /// <summary>
    /// Gets the reader.
    /// </summary>
    public static Win32PointerReader Instance { get; } = new();

    /// <inheritdoc/>
    public bool TryGetType(uint pointerId, out POINTER_INPUT_TYPE type)
    {
        uint read = 0;
        bool ok = GetPointerType(pointerId, &read) != BOOL.FALSE;
        type = (POINTER_INPUT_TYPE)read;
        return ok;
    }

    /// <inheritdoc/>
    public bool TryGetPenInfo(uint pointerId, out POINTER_PEN_INFO info)
    {
        POINTER_PEN_INFO read;
        bool ok = GetPointerPenInfo(pointerId, &read) != BOOL.FALSE;
        info = read;
        return ok;
    }

    /// <inheritdoc/>
    public bool TryGetTouchInfo(uint pointerId, out POINTER_TOUCH_INFO info)
    {
        POINTER_TOUCH_INFO read;
        bool ok = GetPointerTouchInfo(pointerId, &read) != BOOL.FALSE;
        info = read;
        return ok;
    }

    /// <inheritdoc/>
    public int ReadPenHistory(uint pointerId, Span<POINTER_PEN_INFO> destination)
    {
        uint count = (uint)destination.Length;
        fixed (POINTER_PEN_INFO* entries = destination)
        {
            return GetPointerPenInfoHistory(pointerId, &count, entries) != BOOL.FALSE ? (int)Math.Min(count, (uint)destination.Length) : 0;
        }
    }

    /// <inheritdoc/>
    public int ReadTouchHistory(uint pointerId, Span<POINTER_TOUCH_INFO> destination)
    {
        uint count = (uint)destination.Length;
        fixed (POINTER_TOUCH_INFO* entries = destination)
        {
            return GetPointerTouchInfoHistory(pointerId, &count, entries) != BOOL.FALSE ? (int)Math.Min(count, (uint)destination.Length) : 0;
        }
    }
}
