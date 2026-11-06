using TerraFX.Interop.Windows;

namespace XylemUI.Platform.Windows;

// The window's part of the UIA provider (design §4.4.1, "The shape of P02g"): it passes AccessKit's adapter its WM_GETOBJECT and
// its keyboard focus. AccessKit's adapter that subclasses the window would do this itself; the window procedure passes them on
// instead, as GPUI does (gpui_windows/src/events.rs, lines 836 to 851).
public sealed unsafe partial class Win32Window
{
    private const uint WmGetObject = WM.WM_GETOBJECT;
    private const uint WmSetFocus = WM.WM_SETFOCUS;
    private const uint WmKillFocus = WM.WM_KILLFOCUS;

    /// <summary>
    /// The message that carries a request for an action of assistive technology to the UI thread, the request in <c>lParam</c>
    /// (design §4.4.1, "Threads").
    /// </summary>
    internal const uint WmAccessKitAction = WM.WM_APP + 1;

    /// <summary>
    /// Gets or sets the window's UIA provider, or <see langword="null"/> for none: the window then answers no <c>WM_GETOBJECT</c>
    /// itself, and Windows gives the default.
    /// </summary>
    internal Win32Accessibility? Accessibility { get; set; }

    // Returns whether the message was handled. The focus messages are passed on and left to the rest of the window.
    private bool HandleAccessibilityMessage(uint message, WPARAM wParam, LPARAM lParam, ref LRESULT result)
    {
        if (Accessibility is not { } accessibility)
        {
            // A request that comes after the provider let go of the window is freed, not carried out.
            if (message == WmAccessKitAction)
            {
                AccessKit.ActionRequestFree(lParam);
                return true;
            }

            return false;
        }

        switch (message)
        {
            case WmAccessKitAction:
                accessibility.Perform(lParam);
                return true;
            case WmGetObject:
                return accessibility.HandleGetObject(wParam, lParam, out result);
            case WmSetFocus:
                accessibility.WindowFocusChanged(focused: true);
                return false;
            case WmKillFocus:
                accessibility.WindowFocusChanged(focused: false);
                return false;
            default:
                return false;
        }
    }
}
